import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import {
  ShieldCheckIcon,
  Trash2Icon,
  TriangleAlertIcon,
  UserXIcon,
} from 'lucide-react';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { Link, useNavigate, useParams } from 'react-router';
import { toast } from 'sonner';
import { z } from 'zod';
import { BackLink } from '@/components/back-link/BackLink';
import { ConfirmDialog } from '@/components/confirm-dialog/ConfirmDialog';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { FormFooter } from '@/components/form-footer/FormFooter';
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import {
  Field,
  FieldContent,
  FieldDescription,
  FieldError,
  FieldGroup,
  FieldLabel,
  FieldTitle,
} from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { Skeleton } from '@/components/ui/skeleton';
import { Spinner } from '@/components/ui/spinner';
import { Switch } from '@/components/ui/switch';
import { useUnsavedChangesPrompt } from '@/hooks/useUnsavedChangesPrompt';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import { httpClient } from '@/lib/api/httpClient';
import { cn } from '@/lib/utils';
import { AccessEditor } from './AccessEditor';
import {
  type AccountResponse,
  adminKey,
  useAdministration,
  useRemoveUser,
  whoAmIKey,
} from './api';
import { SessionList } from './SessionList';
import { adminPill, sectionLabel } from './styles';

const listUrl = '/users-groups';

const schema = z.object({
  name: z.string(),
  email: z.email('Enter a valid email address.'),
  username: z.string().trim().min(1, 'Username is required.'),
  password: z
    .string()
    .refine((v) => !v || v.length >= 8, 'Use at least 8 characters.'),
  groupIds: z.array(z.string()),
  extraGrants: z.array(z.string()),
  isEnabled: z.boolean(),
});
type Values = z.infer<typeof schema>;

/** `/users-groups/users/new` adds a user; `/users-groups/users/:id` edits one in place. */
export function UserFormPage() {
  const { id } = useParams();
  const query = useAdministration();
  const isNew = id === 'new';
  const user = isNew ? undefined : query.data?.users.find((u) => u.id === id);
  const back = (
    <BackLink to={listUrl} className="mb-1">
      Back to Users & Groups
    </BackLink>
  );

  if (query.isError && !query.data)
    return (
      <Page>
        <PageHeader above={back} title="User" />
        <ErrorState
          title="Could not load the user"
          onRetry={() => void query.refetch()}
        />
      </Page>
    );
  if (!query.data)
    return (
      <Page>
        <PageHeader above={back} title={isNew ? 'Add user' : 'User'} />
        <Skeleton className="h-64 max-w-3xl" />
      </Page>
    );
  if (!isNew && !user)
    return (
      <Page>
        <PageHeader above={back} title="User" />
        <EmptyState
          icon={<UserXIcon />}
          title="User not found"
          description="It may have been removed."
          action={
            <Button variant="outline" asChild>
              <Link to={listUrl}>Back to Users & Groups</Link>
            </Button>
          }
        />
      </Page>
    );
  // Remount per user so the draft never carries over.
  return <UserForm key={id} user={user} back={back} />;
}

function UserForm({
  user,
  back,
}: {
  user?: AccountResponse;
  back: React.ReactNode;
}) {
  const { data } = useAdministration();
  const client = useQueryClient();
  const navigate = useNavigate();
  const removeUser = useRemoveUser();
  const [removeOpen, setRemoveOpen] = useState(false);
  const groups = data?.groups ?? [];
  const id = user?.id;
  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: {
      name: user?.name ?? '',
      email: user?.email ?? '',
      username: user?.username ?? '',
      password: '',
      groupIds: user?.groups?.map((g) => g.id ?? '') ?? [],
      extraGrants: user?.extraGrants ?? [],
      isEnabled: user?.isEnabled ?? true,
    },
  });
  const { errors, isSubmitting, isDirty } = form.formState;
  const { ConfirmLeaveDialog, markClean } = useUnsavedChangesPrompt(isDirty);
  const name = form.watch('name');
  const groupIds = form.watch('groupIds');
  const chosen = groups.filter((g) => groupIds.includes(g.id ?? ''));
  const areaAdminOnly = new Set(
    (data?.areas ?? []).filter((a) => a.administratorOnly).map((a) => a.key),
  );
  const inherited = chosen.some((g) => g.isProtected)
    ? (data?.permissions ?? [])
        .filter((p) => !areaAdminOnly.has(p.area))
        .map((p) => p.key ?? '')
    : chosen.flatMap((g) => g.grants ?? []);

  const refresh = () =>
    Promise.all([
      client.invalidateQueries({ queryKey: [adminKey] }),
      client.invalidateQueries({ queryKey: whoAmIKey }),
    ]);

  const submit = form.handleSubmit(async (value) => {
    if (!id && !value.password) {
      form.setError('password', { message: 'Set an initial password.' });
      return;
    }
    const from = new Set(inherited);
    const body = {
      name: value.name.trim() || null,
      email: value.email,
      username: value.username,
      groupIds: value.groupIds,
      extraGrants: value.extraGrants.filter((k) => !from.has(k)),
      isEnabled: value.isEnabled,
    };
    try {
      if (id) {
        await httpClient(`${adminKey}/users/${id}`, {
          method: 'PUT',
          body: JSON.stringify(body),
        });
        if (value.password) {
          try {
            await httpClient(`${adminKey}/users/${id}/reset-password`, {
              method: 'POST',
              body: JSON.stringify({ password: value.password }),
            });
          } catch (e) {
            // The user is saved; the draft stays dirty so Save retries the reset.
            await refresh();
            toast.error(
              await extractErrorDetail(
                e,
                'User saved, but the password was not reset',
              ),
            );
            return;
          }
        }
        form.reset({ ...value, password: '' });
        await refresh();
        toast.success('User saved');
      } else {
        const created = await httpClient<{ data?: { id?: string } }>(
          `${adminKey}/users`,
          {
            method: 'POST',
            body: JSON.stringify({ ...body, password: value.password }),
          },
        );
        markClean();
        await refresh();
        toast.success('User created');
        const newId = created?.data?.id;
        navigate(newId ? `${listUrl}/users/${newId}` : listUrl, {
          replace: true,
        });
      }
    } catch (e) {
      toast.error(await extractErrorDetail(e, 'Could not save the user'));
    }
  });

  const text = (
    key: 'name' | 'email' | 'username' | 'password',
    label: string,
    placeholder: string,
    hint?: string,
  ) => (
    <Field data-invalid={!!errors[key]}>
      <FieldLabel htmlFor={`user-${key}`}>{label}</FieldLabel>
      <Input
        id={`user-${key}`}
        type={key === 'email' ? 'email' : 'text'}
        autoComplete="off"
        placeholder={placeholder}
        aria-invalid={!!errors[key]}
        {...form.register(key)}
      />
      {hint && <FieldDescription>{hint}</FieldDescription>}
      <FieldError errors={[errors[key]]} />
    </Field>
  );

  return (
    <Page className="flex-1">
      <PageHeader
        above={back}
        title={id ? name || user?.email || 'Unnamed user' : 'Add user'}
        description={id ? user?.email : undefined}
        action={
          user && (
            <Button
              type="button"
              variant="destructive"
              onClick={() => setRemoveOpen(true)}
            >
              <Trash2Icon data-icon="inline-start" />
              Remove
            </Button>
          )
        }
      />
      <form
        noValidate
        className="flex flex-1 flex-col gap-6"
        onSubmit={(e) => void submit(e)}
      >
        <FieldGroup className="max-w-3xl flex-1 gap-6">
          <div className="grid gap-4 md:grid-cols-2">
            {text('name', 'Full name', 'e.g. Mei Ling Tan')}
            {text('email', 'Email', 'name@example.com')}
            {text('username', 'Username', 'username')}
            {id
              ? text(
                  'password',
                  'Reset password',
                  'Leave blank to keep current',
                  'Leave blank to keep the current password. A new one signs the user out everywhere.',
                )
              : text(
                  'password',
                  'Temporary password',
                  'Set an initial password',
                  'Share it with the user. They can change it in their profile.',
                )}
          </div>
          <div className="flex items-center justify-between gap-4 rounded-md border px-3.5 py-3">
            <div className="min-w-0">
              <label
                htmlFor="user-enabled"
                className="text-[13.5px] font-medium"
              >
                Account enabled
              </label>
              <p className="mt-0.5 text-xs text-muted-foreground">
                Off signs the user out everywhere and blocks sign-in.
              </p>
            </div>
            <Switch
              id="user-enabled"
              checked={form.watch('isEnabled')}
              onCheckedChange={(v) =>
                form.setValue('isEnabled', v, { shouldDirty: true })
              }
            />
          </div>

          <section className="flex flex-col gap-3">
            <div className={sectionLabel}>Group membership</div>
            {groupIds.length === 0 && (
              <div className="flex items-start gap-2 rounded-md border border-amber/40 bg-amber-soft px-3 py-2.5 text-[12.5px] text-amber">
                <TriangleAlertIcon className="mt-px size-3.5 shrink-0" />
                With no groups this user can sign in but sees an empty app —
                every screen is hidden.
              </div>
            )}
            <div className="grid gap-1.5 md:grid-cols-2">
              {groups.map((g) => (
                <FieldLabel
                  key={g.id}
                  htmlFor={`group-${g.id}`}
                  className="has-data-checked:border-primary"
                >
                  <Field orientation="horizontal">
                    <Checkbox
                      id={`group-${g.id}`}
                      checked={groupIds.includes(g.id ?? '')}
                      onCheckedChange={(v) =>
                        form.setValue(
                          'groupIds',
                          v
                            ? [...groupIds, g.id ?? '']
                            : groupIds.filter((x) => x !== g.id),
                          { shouldDirty: true },
                        )
                      }
                    />
                    <FieldContent>
                      <FieldTitle className="text-[13px]">
                        {g.name}
                        {g.isProtected && (
                          <span
                            className={cn(
                              adminPill,
                              'px-1.5 py-px text-[10.5px]',
                            )}
                          >
                            <ShieldCheckIcon className="size-[11px]" />
                            Administrator
                          </span>
                        )}
                      </FieldTitle>
                      {g.description && (
                        <FieldDescription className="text-xs">
                          {g.description}
                        </FieldDescription>
                      )}
                    </FieldContent>
                  </Field>
                </FieldLabel>
              ))}
            </div>
          </section>

          {!user?.isAdministrator && (
            <section className="flex flex-col gap-3">
              <div className={sectionLabel}>Extra permissions</div>
              <p className="text-xs text-muted-foreground">
                What this user's groups grant is already on, and cannot be taken
                away here. Raise a level or switch on an ability to give this
                user alone more.
              </p>
              <AccessEditor
                areas={data?.areas ?? []}
                permissions={data?.permissions ?? []}
                value={form.watch('extraGrants')}
                inherited={inherited}
                disabled={isSubmitting}
                onChange={(v) =>
                  form.setValue('extraGrants', v, { shouldDirty: true })
                }
              />
            </section>
          )}

          {id && (
            <section className="flex flex-col gap-3">
              <div className={sectionLabel}>Sessions</div>
              <p className="text-xs text-muted-foreground">
                Only the user can sign in. You can revoke theirs.
              </p>
              <SessionList
                userId={id}
                emptyText="This user has no active sessions."
              />
            </section>
          )}
        </FieldGroup>
        <FormFooter
          end={
            <>
              <Button
                type="button"
                variant="outline"
                onClick={() => navigate(listUrl)}
              >
                Cancel
              </Button>
              <Button
                type="submit"
                disabled={isSubmitting || (!!id && !isDirty)}
              >
                {isSubmitting && <Spinner data-icon="inline-start" />}
                {isSubmitting ? 'Saving…' : id ? 'Save' : 'Add user'}
              </Button>
            </>
          }
        />
      </form>
      {user && (
        <ConfirmDialog
          open={removeOpen}
          onOpenChange={setRemoveOpen}
          title={`Remove ${user.name || user.email}?`}
          description="This cannot be undone."
          confirmLabel="Remove"
          destructive
          isPending={removeUser.isPending}
          closeOnConfirm={false}
          onConfirm={() => {
            if (!user.id) return;
            removeUser
              .mutateAsync(user.id)
              .then(() => {
                markClean();
                toast.success('User removed');
                setRemoveOpen(false);
                navigate(listUrl, { replace: true });
              })
              .catch(async (e) =>
                toast.error(
                  await extractErrorDetail(e, 'Could not remove the user'),
                ),
              );
          }}
        />
      )}
      <ConfirmLeaveDialog />
    </Page>
  );
}
