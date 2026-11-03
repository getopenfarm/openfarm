import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { useNavigate } from 'react-router';
import { z } from 'zod';
import { BackLink } from '@/components/back-link/BackLink';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { FormFooter } from '@/components/form-footer/FormFooter';
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Alert, AlertDescription } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import {
  Field,
  FieldError,
  FieldGroup,
  FieldLabel,
} from '@/components/ui/field';
import { Input } from '@/components/ui/input';
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
    <Page className="min-h-full flex-1">
      <PageHeader
        title={credential ? 'Your token is ready' : 'Add access token'}
        description="Let a script or an AI assistant use OpenFarm with only the permissions you choose."
        above={<BackLink to={home}>Access tokens</BackLink>}
      />
      {credential ? (
        <TokenCredential
          credential={credential}
          onDone={() => {
            setCredential(null);
            navigate(home, { replace: true });
          }}
        />
      ) : !options.data ? (
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
          className="flex flex-1 flex-col gap-6"
          onSubmit={form.handleSubmit(submit)}
        >
          <fieldset disabled={isSubmitting} className="contents">
            <FieldGroup>
              <Field data-invalid={!!errors.name}>
                <FieldLabel htmlFor="token-name">Name</FieldLabel>
                <Input
                  id="token-name"
                  maxLength={100}
                  placeholder="e.g. Inventory reporting"
                  {...form.register('name')}
                  aria-invalid={!!errors.name}
                  aria-describedby={
                    errors.name ? 'token-name-error' : undefined
                  }
                />
                {errors.name && (
                  <FieldError id="token-name-error">
                    {errors.name.message}
                  </FieldError>
                )}
              </Field>
              <Field>
                <FieldLabel htmlFor="token-expiry">
                  Expires (end of day, UTC)
                </FieldLabel>
                <Input
                  id="token-expiry"
                  className="w-48"
                  type="date"
                  min={localDay(tomorrow)}
                  {...form.register('expiresOn')}
                  aria-describedby="token-expiry-note"
                />
                <p
                  id="token-expiry-note"
                  className="text-xs text-muted-foreground"
                >
                  {expiry
                    ? 'The credential stops working at the end of this date, UTC. Clear the date for no expiry.'
                    : 'Never expires. Anyone who gets this credential can use it until you revoke it.'}
                </p>
              </Field>
              <section
                className="flex flex-col gap-3"
                aria-labelledby="token-permissions"
              >
                <h3 id="token-permissions" className="text-sm font-semibold">
                  Permissions
                </h3>
                <p className="text-xs text-muted-foreground">
                  Choose a level per area, or Custom for individual abilities.
                  You can only hand on access you currently hold. Account
                  administration requires a browser session.
                </p>
                {options.data.available?.length ? (
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
              </section>
              <section
                className="rounded-lg border bg-card p-4"
                aria-label="This token can"
              >
                <h3 className="mb-2 text-sm font-semibold">This token can</h3>
                {selected.length === 0 ? (
                  <p className="text-xs text-muted-foreground">
                    Nothing yet. Choose at least one permission.
                  </p>
                ) : (
                  <ul className="space-y-2 text-xs">
                    {options.data.areas
                      ?.filter((a) =>
                        options.data.permissions?.some(
                          (p) =>
                            p.area === a.key && selected.includes(p.key ?? ''),
                        ),
                      )
                      .map((a) => (
                        <li key={a.key}>
                          <strong>{a.label}: </strong>
                          {describeArea(
                            (options.data?.permissions?.filter(
                              (p) => p.area === a.key,
                            ) ?? []) as Ability[],
                            a.noun ?? '',
                            selected,
                          )}
                        </li>
                      ))}
                  </ul>
                )}
                <p className="mt-3 text-xs text-muted-foreground">
                  Never more than your current access at the moment it is used.
                </p>
              </section>
            </FieldGroup>
          </fieldset>
          {error && (
            <Alert variant="destructive">
              <AlertDescription>{error}</AlertDescription>
            </Alert>
          )}
          <FormFooter
            end={
              <>
                <Button
                  type="button"
                  variant="outline"
                  disabled={isSubmitting}
                  onClick={() => navigate(home)}
                >
                  Cancel
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
        </form>
      )}
      <ConfirmLeaveDialog />
    </Page>
  );
}
