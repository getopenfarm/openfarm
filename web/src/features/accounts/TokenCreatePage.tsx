import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { ShieldCheckIcon } from 'lucide-react';
import { useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { useNavigate } from 'react-router';
import { z } from 'zod';
import { BackLink } from '@/components/back-link/BackLink';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { FormFooter } from '@/components/form-footer/FormFooter';
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Alert, AlertDescription } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { DatePicker } from '@/components/ui/date-picker';
import {
  Field,
  FieldError,
  FieldGroup,
  FieldLabel,
} from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { Separator } from '@/components/ui/separator';
import { Skeleton } from '@/components/ui/skeleton';
import { Spinner } from '@/components/ui/spinner';
import { useUnsavedChangesPrompt } from '@/hooks/useUnsavedChangesPrompt';
import { AccessEditor } from './AccessEditor';
import { type Ability, describeArea } from './describe';
import { TokenCredential } from './TokenCredential';
import { createToken, tokenError, tokenKey, useTokenOptions } from './tokenApi';

const home = '/profile?tab=tokens';
const schema = z.object({
  name: z.string().trim().min(1, 'Give the token a name.').max(100),
  expiresOn: z.string(),
  permissions: z.array(z.string()).min(1, 'Choose at least one permission.'),
});
// Calendar months, clamped to the destination month (e.g. August 31 → February 28).
export function defaultTokenExpiry(now = new Date()) {
  const end = new Date(now.getFullYear(), now.getMonth() + 7, 0);
  end.setDate(Math.min(now.getDate(), end.getDate()));
  return localDay(end);
}
function localDay(date: Date) {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
}

export function TokenCreatePage() {
  const options = useTokenOptions();
  const client = useQueryClient();
  const navigate = useNavigate();
  const [credential, setCredential] = useState<string | null>(null);
  const [error, setError] = useState('');
  const form = useForm<z.infer<typeof schema>>({
    resolver: zodResolver(schema),
    defaultValues: {
      name: '',
      expiresOn: defaultTokenExpiry(),
      permissions: [],
    },
  });
  const { isDirty, isSubmitting, errors } = form.formState;
  const { ConfirmLeaveDialog, markClean } = useUnsavedChangesPrompt(
    isDirty && !credential,
  );
  const selected = form.watch('permissions');
  const expiry = form.watch('expiresOn');
  const tomorrow = new Date();
  tomorrow.setDate(tomorrow.getDate() + 1);
  async function submit(input: z.infer<typeof schema>) {
    setError('');
    try {
      const response = await createToken({
        ...input,
        expiresOn: input.expiresOn || null,
      });
      setCredential(response.credential ?? null);
      markClean();
      void client.invalidateQueries({ queryKey: tokenKey() });
    } catch (e) {
      setError(await tokenError(e, 'Could not create token.'));
    }
  }
  return (
    <Page className="min-h-full flex-1 gap-[18px]">
      <PageHeader
        className="[&_h2]:mb-1.5 [&_p]:text-[12.5px]"
        title={credential ? form.getValues('name') : 'New access token'}
        description={
          credential
            ? 'Your token is ready.'
            : 'A token lets a script or an AI assistant use OpenFarm as you — with only what you choose here.'
        }
        above={<BackLink to={home}>Access tokens</BackLink>}
      />
      {!options.data ? (
        options.isError ? (
          <ErrorState
            title="Could not load permissions"
            onRetry={() => void options.refetch()}
          />
        ) : (
          <Skeleton className="h-48" />
        )
      ) : (
        <form
          className="flex flex-1 flex-col gap-4"
          onSubmit={form.handleSubmit(submit)}
        >
          {error && (
            <Alert variant="destructive">
              <AlertDescription>{error}</AlertDescription>
            </Alert>
          )}
          <div className="grid items-start gap-[18px] pb-10 min-[1100px]:grid-cols-[minmax(0,1fr)_320px]">
            <div className="flex min-w-0 flex-col gap-4">
              {credential ? (
                <TokenCredential
                  credential={credential}
                  onDone={() => {
                    setCredential(null);
                    navigate(home, { replace: true });
                  }}
                />
              ) : (
                <fieldset disabled={isSubmitting} className="contents">
                  <TokenPanel title="Token" titleId="token-details">
                    <FieldGroup className="gap-4">
                      <Field data-invalid={!!errors.name} className="gap-1.5">
                        <FieldLabel
                          htmlFor="token-name"
                          className="text-[12.5px]"
                        >
                          Name
                        </FieldLabel>
                        <Input
                          id="token-name"
                          className="h-9 text-[13.5px]"
                          maxLength={100}
                          placeholder="e.g. Claude — farm reporting"
                          {...form.register('name')}
                          aria-invalid={!!errors.name}
                          aria-describedby={
                            errors.name ? 'token-name-error' : 'token-name-note'
                          }
                        />
                        <p
                          id="token-name-note"
                          className="text-[11.5px] text-muted-foreground"
                        >
                          What uses this token, so you can tell it apart later.
                        </p>
                        {errors.name && (
                          <FieldError id="token-name-error">
                            {errors.name.message}
                          </FieldError>
                        )}
                      </Field>
                      <Field className="gap-1.5">
                        <FieldLabel
                          htmlFor="token-expiry"
                          className="text-[12.5px]"
                        >
                          Expires
                        </FieldLabel>
                        <Controller
                          control={form.control}
                          name="expiresOn"
                          render={({ field }) => (
                            <DatePicker
                              id="token-expiry"
                              {...field}
                              min={localDay(tomorrow)}
                              disabled={isSubmitting}
                              placeholder="Never expires"
                              clearLabel="Remove the expiry date"
                              describedBy="token-expiry-note"
                            />
                          )}
                        />
                        <p
                          id="token-expiry-note"
                          className={
                            expiry
                              ? 'text-[11.5px] text-muted-foreground'
                              : 'text-[11.5px] text-amber'
                          }
                        >
                          {expiry
                            ? `Works until the end of ${new Intl.DateTimeFormat(undefined, { dateStyle: 'long', timeZone: 'UTC' }).format(new Date(`${expiry}T00:00:00Z`))} (UTC), then stops.`
                            : 'Anyone who gets this token can use it until you revoke it.'}
                        </p>
                      </Field>
                    </FieldGroup>
                  </TokenPanel>
                  <TokenPanel title="Permissions" titleId="token-permissions">
                    {options.data.available?.length ? (
                      <>
                        <p className="mb-3 text-[12.5px] text-muted-foreground">
                          Pick a level for each area. Choose Custom to pick
                          single abilities. Areas you can't hand on are greyed
                          out.
                        </p>
                        <AccessEditor
                          areas={options.data.areas ?? []}
                          permissions={options.data.permissions ?? []}
                          available={options.data.available}
                          value={selected}
                          onChange={(v) =>
                            form.setValue('permissions', v, {
                              shouldDirty: true,
                              shouldValidate: true,
                            })
                          }
                          disabled={isSubmitting}
                        />
                      </>
                    ) : (
                      <EmptyState
                        title="Nothing to hand on yet"
                        description="Ask an administrator to grant your account access."
                      />
                    )}
                    {errors.permissions && (
                      <FieldError role="alert">
                        {errors.permissions.message}
                      </FieldError>
                    )}
                  </TokenPanel>
                </fieldset>
              )}
            </div>
            <aside className="min-w-0" aria-label="Token access summary">
              <TokenPanel title="This token can" titleId="token-summary">
                {selected.length === 0 ? (
                  <p className="text-[12.5px] leading-[1.45] text-muted-foreground">
                    Nothing yet. Pick a level for at least one area.
                  </p>
                ) : (
                  <ul className="flex flex-col gap-2.5" aria-live="polite">
                    {options.data.areas
                      ?.filter((a) =>
                        options.data.permissions?.some(
                          (p) =>
                            p.area === a.key && selected.includes(p.key ?? ''),
                        ),
                      )
                      .map((a) => (
                        <li key={a.key} className="flex flex-col gap-px">
                          <span className="text-[13px] font-semibold">
                            {a.label}
                          </span>
                          <span className="text-[12.5px] leading-[1.45] text-muted-foreground">
                            {describeArea(
                              (options.data?.permissions?.filter(
                                (p) => p.area === a.key,
                              ) ?? []) as Ability[],
                              a.noun ?? '',
                              selected,
                            )}
                          </span>
                        </li>
                      ))}
                  </ul>
                )}
                <Separator className="mt-3.5 mb-3" />
                <p className="flex items-start gap-1.5 text-xs text-muted-foreground">
                  <ShieldCheckIcon className="mt-px size-3.5 shrink-0" />
                  <span>
                    Never more than you can do yourself at the moment it is
                    used.
                  </span>
                </p>
              </TokenPanel>
            </aside>
          </div>
          {!credential && isDirty && (
            <FormFooter
              className="mx-0 mb-0 border-0 px-0 [&>div]:w-full"
              end={
                <>
                  <span className="mr-auto text-[12.5px] text-muted-foreground">
                    Not created yet
                  </span>
                  <Button
                    type="button"
                    variant="outline"
                    disabled={isSubmitting}
                    onClick={() => {
                      form.reset();
                      setError('');
                    }}
                  >
                    Discard
                  </Button>
                  <Button
                    type="submit"
                    disabled={isSubmitting || !options.data.available?.length}
                  >
                    {isSubmitting && <Spinner data-icon="inline-start" />}
                    {isSubmitting ? 'Creating…' : 'Create token'}
                  </Button>
                </>
              }
            />
          )}
        </form>
      )}
      <ConfirmLeaveDialog />
    </Page>
  );
}

function TokenPanel({
  title,
  titleId,
  children,
}: {
  title: string;
  titleId: string;
  children: React.ReactNode;
}) {
  return (
    <Card
      role="region"
      aria-labelledby={titleId}
      className="min-w-0 gap-3 rounded-lg border py-4 shadow-sm ring-0"
    >
      <CardHeader className="px-[18px]">
        <CardTitle
          id={titleId}
          className="text-[11px] font-semibold tracking-[0.05em] text-muted-foreground uppercase"
        >
          {title}
        </CardTitle>
      </CardHeader>
      <CardContent className="px-[18px]">{children}</CardContent>
    </Card>
  );
}
