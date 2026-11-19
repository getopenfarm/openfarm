import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { CopyIcon } from 'lucide-react';
import { useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { Link, useSearchParams } from 'react-router';
import { toast } from 'sonner';
import { z } from 'zod';
import { ErrorState } from '@/components/empty-state/EmptyState';
import { Alert, AlertDescription } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { FieldError, FieldSet } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Skeleton } from '@/components/ui/skeleton';
import { Spinner } from '@/components/ui/spinner';
import { Switch } from '@/components/ui/switch';
import { useUnsavedChangesPrompt } from '@/hooks/useUnsavedChangesPrompt';
import { apiUrl } from '@/lib/api/apiUrl';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import type { AuthenticationSettingsResponse } from '@/lib/api/generated/endpoints.schemas';
import { httpClient } from '@/lib/api/httpClient';
import { useAuthorizedQuery } from '@/lib/auth/useAuthorizedQuery';
import {
  SettingsHint,
  SettingsRow,
  SettingsRows,
  SettingsSection,
  SettingsSubhead,
} from './SettingsParts';

const key = '/api/v1/admin/settings/authentication';
const schema = z
  .object({
    oidcEnabled: z.boolean(),
    internalAuthenticationEnabled: z.boolean(),
    displayName: z.string().trim().min(1).max(80),
    authority: z.string().trim().max(2048),
    clientId: z.string().trim().max(256),
    clientCredential: z.string().max(4096),
    sessionHours: z.number().int().min(1).max(24),
    recoveryUserIds: z.array(z.string()),
    jitEnabled: z.boolean(),
    requireVerifiedEmail: z.boolean(),
    defaultGroup: z.string().min(1),
  })
  .superRefine((value, ctx) => {
    if (!value.oidcEnabled && !value.internalAuthenticationEnabled)
      ctx.addIssue({
        code: 'custom',
        path: ['internalAuthenticationEnabled'],
        message: 'Enable at least one login method.',
      });
    if (value.oidcEnabled) {
      try {
        const url = new URL(value.authority);
        const loopback =
          url.hostname === 'localhost' ||
          url.hostname.startsWith('127.') ||
          url.hostname === '[::1]';
        if (
          (url.protocol !== 'https:' &&
            !(url.protocol === 'http:' && loopback)) ||
          url.username ||
          url.password ||
          url.search ||
          url.hash
        )
          throw new Error();
      } catch {
        ctx.addIssue({
          code: 'custom',
          path: ['authority'],
          message:
            'Enter an HTTPS issuer URL. HTTP is allowed only on localhost.',
        });
      }
      if (!value.clientId)
        ctx.addIssue({
          code: 'custom',
          path: ['clientId'],
          message: 'Enter the provider client ID.',
        });
    }
    if (!value.internalAuthenticationEnabled && !value.recoveryUserIds.length)
      ctx.addIssue({
        code: 'custom',
        path: ['recoveryUserIds'],
        message: 'Select at least one local recovery administrator.',
      });
  });
type Values = z.infer<typeof schema>;
function defaults(data: AuthenticationSettingsResponse): Values {
  return {
    oidcEnabled: data.oidcEnabled ?? false,
    internalAuthenticationEnabled: data.internalAuthenticationEnabled ?? true,
    displayName: data.displayName ?? 'authentik',
    authority: data.authority ?? '',
    clientId: data.clientId ?? '',
    clientCredential: '',
    sessionHours: data.sessionHours ?? 8,
    recoveryUserIds: (data.recoveryUserIds ?? []).filter((id) =>
      (data.recoveryAccounts ?? []).some((account) => account.id === id),
    ),
    jitEnabled: data.jitEnabled ?? false,
    requireVerifiedEmail: data.requireVerifiedEmail ?? true,
    defaultGroup: data.defaultGroup ?? 'Viewers',
  };
}

// Hidden controls retain their draft. Only enabled sections contribute edits on Save.
function activeValues(
  draft: Values,
  data: AuthenticationSettingsResponse,
): Values {
  const saved = defaults(data);
  const active = draft.oidcEnabled ? draft : saved;
  const jit = draft.oidcEnabled && draft.jitEnabled;
  return {
    ...active,
    oidcEnabled: draft.oidcEnabled,
    internalAuthenticationEnabled: draft.internalAuthenticationEnabled,
    recoveryUserIds: draft.recoveryUserIds,
    jitEnabled: jit,
    requireVerifiedEmail: jit
      ? draft.requireVerifiedEmail
      : saved.requireVerifiedEmail,
    defaultGroup: jit ? draft.defaultGroup : saved.defaultGroup,
  };
}

export function AuthenticationSettingsPage() {
  const [search] = useSearchParams();
  const query = useAuthorizedQuery({
    refetchOnWindowFocus: false,
    queryKey: [key],
    queryFn: ({ signal }) =>
      httpClient<{ data: AuthenticationSettingsResponse }>(key, { signal }),
  });
  return (
    <SettingsSection
      title="Authentication"
      description="Connect an identity provider and manage account provisioning."
    >
      {search.get('test') && query.data && (
        <Alert
          role="status"
          variant={
            search.get('test') === 'success' && query.data.data.oidcTested
              ? 'default'
              : 'destructive'
          }
        >
          <AlertDescription>
            {search.get('test') === 'success' && query.data.data.oidcTested
              ? 'Sign-in test passed. Your administrator session is still active.'
              : search.get('ssoReason') === 'provider_configuration'
                ? 'Could not load or use the sign-in provider configuration. Check the Issuer URL, provider connectivity, and OpenFarm server logs.'
                : 'Sign-in test failed. Check the provider setup and link this administrator account in My profile → Security.'}
          </AlertDescription>
        </Alert>
      )}
      {query.data ? (
        <AuthenticationForm
          key={query.data.data.revision}
          data={query.data.data}
        />
      ) : query.isError ? (
        <ErrorState
          title="Could not load authentication settings"
          onRetry={() => void query.refetch()}
        />
      ) : (
        <Skeleton className="h-40" />
      )}
    </SettingsSection>
  );
}

function AuthenticationForm({
  data,
}: {
  data: AuthenticationSettingsResponse;
}) {
  const client = useQueryClient();
  const [error, setError] = useState('');
  const form = useForm<Values>({
    resolver: (draft, context, options) =>
      zodResolver(
        schema.superRefine((draft, ctx) => {
          if (
            draft.oidcEnabled &&
            !draft.clientCredential.trim() &&
            (!data.clientCredentialConfigured ||
              draft.authority !== (data.authority ?? '') ||
              draft.clientId !== (data.clientId ?? ''))
          )
            ctx.addIssue({
              code: 'custom',
              path: ['clientCredential'],
              message: 'Enter the provider client credential.',
            });
        }),
      )(activeValues(draft, data), context, options),
    defaultValues: defaults(data),
  });
  const { ConfirmLeaveDialog } = useUnsavedChangesPrompt(
    form.formState.isDirty,
  );
  const values = form.watch();
  const save = useMutation({
    mutationFn: (draft: Values) =>
      httpClient<{ data: AuthenticationSettingsResponse }>(key, {
        method: 'PUT',
        body: JSON.stringify({
          revision: data.revision,
          settings: {
            oidcEnabled: draft.oidcEnabled,
            internalAuthenticationEnabled: draft.internalAuthenticationEnabled,
            displayName: draft.displayName,
            authority: draft.authority || null,
            clientId: draft.clientId || null,
            clientSecret: draft.clientCredential || null,
            sessionHours: draft.sessionHours,
            recoveryUserIds: draft.recoveryUserIds,
            jitEnabled: draft.jitEnabled,
            requireVerifiedEmail: draft.requireVerifiedEmail,
            defaultGroup: draft.defaultGroup,
          },
        }),
      }),
    onSuccess: (result) => {
      form.reset(defaults(result.data));
      client.setQueryData([key], result);
      void client.invalidateQueries({ queryKey: ['auth', 'providers'] });
      void client.invalidateQueries({ queryKey: ['/api/v1/auth/whoami'] });
      toast.success('Settings saved.');
    },
    onError: async (failure) =>
      setError(
        await extractErrorDetail(
          failure,
          'Cannot save authentication settings. Reload the page and try again.',
        ),
      ),
  });
  const test = useMutation({
    mutationFn: () =>
      httpClient<{ data: { redirectUri: string } }>(`${key}/test`, {
        method: 'POST',
      }),
    onSuccess: (result) => {
      window.location.assign(apiUrl(result.data.redirectUri));
    },
    onError: async (failure) =>
      setError(
        await extractErrorDetail(failure, 'Could not start the sign-in test.'),
      ),
  });
  const disabled = test.isPending || save.isPending || !!data.recoveryMode;
  const errors = form.formState.errors;
  function toggle(
    name:
      | 'oidcEnabled'
      | 'internalAuthenticationEnabled'
      | 'jitEnabled'
      | 'requireVerifiedEmail',
    label: string,
    help: string,
  ) {
    return (
      <SettingsRow
        label={label}
        htmlFor={`authentication-${name}`}
        help={<span id={`authentication-${name}-help`}>{help}</span>}
        disabled={disabled}
        compactControl
      >
        <Controller
          control={form.control}
          name={name}
          render={({ field }) => (
            <Switch
              id={`authentication-${name}`}
              ref={field.ref}
              checked={field.value}
              disabled={disabled}
              aria-describedby={`authentication-${name}-help`}
              onCheckedChange={field.onChange}
            />
          )}
        />
      </SettingsRow>
    );
  }
  function input(
    name:
      | 'displayName'
      | 'authority'
      | 'clientId'
      | 'clientCredential'
      | 'sessionHours',
    label: string,
    help?: string,
    type = 'text',
  ) {
    return (
      <SettingsRow
        label={label}
        htmlFor={`authentication-${name}`}
        wide
        disabled={disabled}
        invalid={!!errors[name]}
        help={
          help ? (
            <span id={`authentication-${name}-help`}>{help}</span>
          ) : undefined
        }
      >
        <Input
          id={`authentication-${name}`}
          type={type}
          className={type === 'number' ? 'w-24' : undefined}
          autoComplete={type === 'password' ? 'new-password' : 'off'}
          disabled={disabled}
          min={type === 'number' ? 1 : undefined}
          max={name === 'sessionHours' ? 24 : undefined}
          aria-invalid={!!errors[name]}
          aria-describedby={`authentication-${name}-help authentication-${name}-error`}
          {...form.register(name, { valueAsNumber: type === 'number' })}
        />
        <FieldError id={`authentication-${name}-error`}>
          {errors[name]?.message}
        </FieldError>
      </SettingsRow>
    );
  }
  function groupSelect(name: 'defaultGroup', id: string) {
    const error = form.getFieldState(name, form.formState).error;
    return (
      <>
        <Controller
          control={form.control}
          name={name}
          render={({ field }) => (
            <Select
              value={field.value}
              onValueChange={field.onChange}
              disabled={disabled}
            >
              <SelectTrigger
                id={id}
                ref={field.ref}
                className="w-full"
                aria-invalid={!!error}
                aria-describedby={error ? `${id}-error` : undefined}
              >
                <SelectValue placeholder="Select group" />
              </SelectTrigger>
              <SelectContent>
                <SelectGroup>
                  {(data.groups ?? []).map((g) => (
                    <SelectItem key={g.id} value={g.name ?? ''}>
                      {g.name}
                    </SelectItem>
                  ))}
                </SelectGroup>
              </SelectContent>
            </Select>
          )}
        />
        <FieldError id={`${id}-error`}>{error?.message}</FieldError>
      </>
    );
  }

  function address(
    label: string,
    value: string | null | undefined,
    help?: string,
  ) {
    if (!value) return null;
    return (
      <SettingsRow label={label} wide help={help}>
        <div className="flex items-start gap-2">
          <code className="min-w-0 flex-1 break-all text-xs">{value}</code>
          <Button
            type="button"
            variant="ghost"
            size="icon-sm"
            aria-label={`Copy ${label.toLowerCase()}`}
            onClick={() =>
              void navigator.clipboard.writeText(value).then(
                () => toast.success(`${label} copied.`),
                () => toast.error(`Could not copy ${label.toLowerCase()}.`),
              )
            }
          >
            <CopyIcon />
          </Button>
        </div>
      </SettingsRow>
    );
  }

  return (
    <form
      className="flex flex-col gap-4"
      onSubmit={form.handleSubmit((draft) => {
        if (!disabled) {
          setError('');
          save.mutate(draft);
        }
      })}
    >
      {data.recoveryMode && (
        <Alert role="status">
          <AlertDescription>
            Deployment recovery mode is active. Turn it off before changing
            authentication settings.
          </AlertDescription>
        </Alert>
      )}
      {values.oidcEnabled && !data.callbackUrl && (
        <Alert role="status">
          <AlertDescription>
            Set an HTTPS{' '}
            <Link className="underline" to="/settings/server">
              public URL
            </Link>{' '}
            before enabling single sign-on.
          </AlertDescription>
        </Alert>
      )}
      <SettingsSubhead>Login methods</SettingsSubhead>
      <SettingsRows>
        {toggle(
          'internalAuthenticationEnabled',
          'Internal authentication',
          'Allow local accounts to sign in with a username and password.',
        )}
        {toggle(
          'oidcEnabled',
          'OIDC authentication',
          'Enable provider sign-in and configure account provisioning.',
        )}
      </SettingsRows>
      <FieldError>{errors.internalAuthenticationEnabled?.message}</FieldError>
      {data.oidcEnabled && !values.oidcEnabled && (
        <Alert role="status">
          <AlertDescription>
            Saving will end existing OIDC sessions and disable JIT provisioning.
            Saved provider credentials will be retained. Unsaved edits to hidden
            settings will not be applied.
          </AlertDescription>
        </Alert>
      )}
      {values.oidcEnabled && (
        <>
          <SettingsSubhead>OpenID Connect</SettingsSubhead>
          <SettingsRows>
            {input(
              'displayName',
              'Provider name',
              'Shown on the sign-in button.',
            )}
            {input(
              'authority',
              'Issuer URL',
              'For authentik, use the per-application issuer base URL, including its trailing slash. Do not append /authorize.',
            )}
            {input('clientId', 'Client ID')}
            {input(
              'clientCredential',
              'Client credential',
              data.clientCredentialConfigured
                ? 'Configured. Leave blank to keep the saved credential.'
                : 'Enter the confidential client credential.',
              'password',
            )}
            {input(
              'sessionHours',
              'Session duration (hours)',
              'After this deadline, users must sign in through the provider again.',
              'number',
            )}
          </SettingsRows>
          {(data.callbackUrl || data.logoutUrl) && (
            <SettingsRows>
              {address('Redirect URI', data.callbackUrl)}
              {address(
                'Back-channel logout URI',
                data.logoutUrl,
                'Optional. Register this address with the provider to end OpenFarm sessions centrally.',
              )}
            </SettingsRows>
          )}
          <FieldSet
            aria-label="Provider sign-in test"
            className="gap-2 rounded-lg border bg-card p-3.5 md:p-[18px]"
          >
            <div className="flex flex-wrap items-center gap-3">
              <Button
                type="button"
                variant="outline"
                disabled={
                  disabled || form.formState.isDirty || !data.oidcEnabled
                }
                onClick={() => test.mutate()}
                aria-describedby="authentication-test-status authentication-test-help"
              >
                {test.isPending && <Spinner data-icon="inline-start" />}
                {test.isPending ? 'Starting…' : 'Test sign-in'}
              </Button>
              <p
                id="authentication-test-status"
                className="text-sm text-muted-foreground"
              >
                {data.oidcTested
                  ? 'Saved provider configuration tested.'
                  : 'Saved provider configuration not tested.'}
              </p>
            </div>
            <p
              id="authentication-test-help"
              className="text-sm text-muted-foreground"
            >
              Save the provider and{' '}
              <Link className="underline" to="/profile?tab=security">
                link your administrator account
              </Link>{' '}
              before testing. Your current session stays active. A successful
              test is required before disabling internal authentication.
            </p>
          </FieldSet>
        </>
      )}
      {values.oidcEnabled && (
        <>
          <SettingsSubhead>Account provisioning</SettingsSubhead>
          <SettingsRows>
            {toggle(
              'jitEnabled',
              'Create accounts on first sign-in (JIT)',
              'Create passwordless accounts for new users allowed by the provider.',
            )}
            {values.jitEnabled &&
              toggle(
                'requireVerifiedEmail',
                'Require verified email',
                'Require email_verified=true when creating an account at first sign-in.',
              )}
          </SettingsRows>
          {values.jitEnabled && (
            <SettingsRows>
              <SettingsRow
                label="Default group"
                htmlFor="authentication-defaultGroup"
                wide
                help="Initial access for new accounts. Administrator groups are excluded."
              >
                {groupSelect('defaultGroup', 'authentication-defaultGroup')}
              </SettingsRow>
            </SettingsRows>
          )}
        </>
      )}
      {!values.internalAuthenticationEnabled && (
        <>
          <SettingsSubhead>Recovery access</SettingsSubhead>
          <SettingsHint>
            Regular users must use OIDC. Selected recovery administrators can
            still use passwords. Select administrators whose passwords you have
            tested. Keep one available if the provider is unavailable. Include
            your current administrator when saving from a password session, or
            sign in through OIDC first.
          </SettingsHint>
          <SettingsRows>
            {(data.recoveryAccounts ?? []).map((account) => (
              <SettingsRow
                key={account.id}
                label={account.username}
                htmlFor={`recovery-${account.id}`}
                disabled={disabled}
                compactControl
              >
                <Checkbox
                  id={`recovery-${account.id}`}
                  checked={values.recoveryUserIds.includes(account.id ?? '')}
                  disabled={disabled}
                  aria-invalid={!!errors.recoveryUserIds}
                  aria-describedby={
                    errors.recoveryUserIds
                      ? 'authentication-recovery-error'
                      : undefined
                  }
                  onCheckedChange={(checked) =>
                    form.setValue(
                      'recoveryUserIds',
                      checked
                        ? [...values.recoveryUserIds, account.id ?? '']
                        : values.recoveryUserIds.filter(
                            (id) => id !== account.id,
                          ),
                      { shouldDirty: true, shouldValidate: true },
                    )
                  }
                />
              </SettingsRow>
            ))}
          </SettingsRows>
          <FieldError id="authentication-recovery-error">
            {errors.recoveryUserIds?.message}
          </FieldError>
        </>
      )}
      <SettingsHint>
        Changes apply when you save. Settings in disabled sections keep their
        saved values.
        {values.oidcEnabled && (
          <>
            {' '}
            Changing the provider, client credential, or session duration ends
            existing SSO sessions. JIT changes apply to new account creation.
          </>
        )}
      </SettingsHint>
      {error && (
        <Alert variant="destructive" role="alert">
          <AlertDescription>{error}</AlertDescription>
        </Alert>
      )}
      <div className="flex flex-wrap gap-2">
        <Button type="submit" disabled={!form.formState.isDirty || disabled}>
          {save.isPending && <Spinner data-icon="inline-start" />}
          {save.isPending ? 'Saving…' : 'Save'}
        </Button>
        <Button
          type="button"
          variant="outline"
          disabled={disabled}
          onClick={() => {
            form.reset(defaults(data));
            setError('');
          }}
        >
          Reset changes
        </Button>
      </div>
      <ConfirmLeaveDialog />
    </form>
  );
}
