import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { CopyIcon, PlusIcon, XIcon } from 'lucide-react';
import { useState } from 'react';
import { Controller, useFieldArray, useForm } from 'react-hook-form';
import { Link } from 'react-router';
import { toast } from 'sonner';
import { z } from 'zod';
import { ErrorState } from '@/components/empty-state/EmptyState';
import { Alert, AlertDescription } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { FieldError } from '@/components/ui/field';
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
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import type {
  AuthenticationSettingsResponse,
  GroupSummary,
} from '@/lib/api/generated/endpoints.schemas';
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
// Mirrors OidcSettings.ParseScopes (RFC 6749 scope tokens); the server is the authority, this only gives a clear message.
const maxAdditionalScopes = 10;
const scopeToken = /^[\x21\x23-\x5B\x5D-\x7E]+$/;
// Mirrors ProvisioningSettings.IdentityClaims: the server is the authority, this only gives a clear message.
const identityClaims = new Set([
  'sub',
  'iss',
  'aud',
  'exp',
  'iat',
  'nbf',
  'nonce',
  'sid',
  'auth_time',
  'email',
  'email_verified',
  'preferred_username',
  'name',
  'azp',
  'acr',
  'amr',
  'at_hash',
  'c_hash',
]);
const schema = z
  .object({
    oidcEnabled: z.boolean(),
    internalAuthenticationEnabled: z.boolean(),
    displayName: z.string().trim().max(80),
    authority: z.string().trim().max(2048),
    clientId: z.string().trim().max(256),
    clientCredential: z.string().max(4096),
    recoveryUserIds: z.array(z.string()),
    jitEnabled: z.boolean(),
    requireVerifiedEmail: z.boolean(),
    defaultGroup: z.string().min(1),
    groupMappings: z
      .array(
        z.object({
          externalGroup: z
            .string()
            .trim()
            .min(1, 'Enter the provider group name.')
            .max(256),
          group: z.string().min(1, 'Select a group.'),
        }),
      )
      .max(50),
    groupsClaim: z.string().trim().max(256),
    additionalScopes: z.string().trim(),
  })
  .superRefine((value, ctx) => {
    const scopes = value.additionalScopes.split(' ').filter(Boolean);
    if (
      value.oidcEnabled &&
      value.jitEnabled &&
      (scopes.length > maxAdditionalScopes ||
        scopes.some((s) => s.length > 128 || !scopeToken.test(s)) ||
        scopes.includes('offline_access'))
    )
      ctx.addIssue({
        code: 'custom',
        path: ['additionalScopes'],
        message: `Enter up to ${maxAdditionalScopes} scope names, separated by spaces. ASCII only, no quotes or backslashes. Not offline_access.`,
      });
    if (
      value.oidcEnabled &&
      value.jitEnabled &&
      (!value.groupsClaim ||
        /\s/.test(value.groupsClaim) ||
        identityClaims.has(value.groupsClaim.toLowerCase()))
    )
      ctx.addIssue({
        code: 'custom',
        path: ['groupsClaim'],
        message:
          'Enter a claim that lists groups. No spaces. Not an identity claim such as sub, email or name.',
      });
    const seen = new Set<string>();
    value.groupMappings.forEach((row, index) => {
      const pair = `${row.externalGroup}\n${row.group.toLowerCase()}`;
      if (seen.has(pair))
        ctx.addIssue({
          code: 'custom',
          path: ['groupMappings', index, 'group'],
          message: 'Duplicate mapping.',
        });
      seen.add(pair);
    });
    if (!value.oidcEnabled && !value.internalAuthenticationEnabled)
      ctx.addIssue({
        code: 'custom',
        path: ['internalAuthenticationEnabled'],
        message: 'Turn on at least one sign-in method.',
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
    displayName: data.displayName ?? '',
    authority: data.authority ?? '',
    clientId: data.clientId ?? '',
    clientCredential: '',
    recoveryUserIds: (data.recoveryUserIds ?? []).filter((id) =>
      (data.recoveryAccounts ?? []).some((account) => account.id === id),
    ),
    jitEnabled: data.jitEnabled ?? false,
    requireVerifiedEmail: data.requireVerifiedEmail ?? true,
    defaultGroup: data.defaultGroup ?? 'Viewers',
    groupMappings: (data.groupMappings ?? []).map((m) => ({
      externalGroup: m.externalGroup ?? '',
      group: m.group ?? '',
    })),
    groupsClaim: data.groupsClaim ?? 'groups',
    additionalScopes: data.additionalScopes ?? '',
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
    groupMappings: jit ? draft.groupMappings : saved.groupMappings,
    groupsClaim: jit ? draft.groupsClaim : saved.groupsClaim,
    additionalScopes: jit ? draft.additionalScopes : saved.additionalScopes,
  };
}

export function AuthenticationSettingsPage() {
  const query = useAuthorizedQuery({
    refetchOnWindowFocus: false,
    queryKey: [key],
    queryFn: ({ signal }) =>
      httpClient<{ data: AuthenticationSettingsResponse }>(key, { signal }),
  });
  return (
    <SettingsSection
      title="Authentication"
      description="Provider sign-in and account provisioning."
    >
      {query.data ? (
        <AuthenticationForm
          key={query.data.data.revision}
          data={query.data.data}
        />
      ) : query.isError ? (
        <ErrorState title="Load failed" onRetry={() => void query.refetch()} />
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
  const groups = data.groups ?? [];
  const mapsProtectedGroup = values.groupMappings.some((row) =>
    groups.some((g) => g.isProtected && g.name === row.group),
  );
  const mappings = useFieldArray({
    control: form.control,
    name: 'groupMappings',
  });
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
            recoveryUserIds: draft.recoveryUserIds,
            jitEnabled: draft.jitEnabled,
            requireVerifiedEmail: draft.requireVerifiedEmail,
            defaultGroup: draft.defaultGroup,
            groupMappings: draft.groupMappings.map((m) => ({
              externalGroup: m.externalGroup.trim(),
              group: m.group,
            })),
            groupsClaim: draft.groupsClaim,
            additionalScopes: draft.additionalScopes,
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
          'Save failed. Reload the page. Try again.',
        ),
      ),
  });
  const disabled = save.isPending || !!data.recoveryMode;
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
      | 'groupsClaim'
      | 'additionalScopes',
    label: string,
    help?: string,
    type = 'text',
    placeholder?: string,
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
          placeholder={placeholder}
          autoComplete={type === 'password' ? 'new-password' : 'off'}
          disabled={disabled}
          aria-invalid={!!errors[name]}
          aria-describedby={`authentication-${name}-help authentication-${name}-error`}
          {...form.register(name)}
        />
        <FieldError id={`authentication-${name}-error`}>
          {errors[name]?.message}
        </FieldError>
      </SettingsRow>
    );
  }
  function groupSelect(
    name: 'defaultGroup' | `groupMappings.${number}.group`,
    options: GroupSummary[],
    id: string,
    label?: string,
  ) {
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
                aria-label={label}
                className="w-full"
                aria-invalid={!!error}
                aria-describedby={error ? `${id}-error` : undefined}
              >
                <SelectValue placeholder="Select group" />
              </SelectTrigger>
              <SelectContent>
                <SelectGroup>
                  {options.map((g) => (
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
                () => toast.error('Copy failed.'),
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
            Recovery mode is on. Turn it off to change authentication settings.
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
            to turn on OIDC.
          </AlertDescription>
        </Alert>
      )}
      <SettingsSubhead>Sign-in methods</SettingsSubhead>
      <SettingsRows>
        {toggle(
          'internalAuthenticationEnabled',
          'Internal authentication',
          'Allow local accounts to sign in with a username and password.',
        )}
        {toggle(
          'oidcEnabled',
          'OIDC authentication',
          'Provider sign-in and account provisioning.',
        )}
      </SettingsRows>
      <FieldError>{errors.internalAuthenticationEnabled?.message}</FieldError>
      {data.oidcEnabled && !values.oidcEnabled && (
        <Alert role="status">
          <AlertDescription>
            Save ends OIDC sessions and turns off JIT. Saved provider
            credentials stay. Unsaved edits to hidden settings are not applied.
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
              'Used in “Sign in with …”. Blank = SSO.',
            )}
            {input(
              'authority',
              'Issuer URL',
              'Copy the exact issuer from the provider, with any trailing slash. Do not add /authorize.',
            )}
            {input('clientId', 'Client ID')}
            {input(
              'clientCredential',
              'Client credential',
              data.clientCredentialConfigured
                ? undefined
                : 'Enter the client credential.',
              'password',
              // The server never returns the credential: dots mark a saved one, blank keeps it.
              data.clientCredentialConfigured ? '••••••••••••' : undefined,
            )}
          </SettingsRows>
          {(data.callbackUrl || data.logoutUrl) && (
            <SettingsRows>
              {address('Redirect URI', data.callbackUrl)}
              {address(
                'Back-channel logout URI',
                data.logoutUrl,
                'Optional. Add this address to the provider. Provider sign-out then ends OpenFarm sessions.',
              )}
            </SettingsRows>
          )}
        </>
      )}
      {values.oidcEnabled && (
        <>
          <SettingsSubhead>Account provisioning</SettingsSubhead>
          <SettingsRows>
            {toggle(
              'jitEnabled',
              'Create accounts on first sign-in (JIT)',
              'Passwordless accounts for users the provider allows.',
            )}
            {values.jitEnabled &&
              toggle(
                'requireVerifiedEmail',
                'Require verified email',
                'Create accounts only if email_verified is true.',
              )}
          </SettingsRows>
          {values.jitEnabled && (
            <SettingsRows>
              <SettingsRow
                label="Default group"
                htmlFor="authentication-defaultGroup"
                wide
                help="Group for new accounts and for users with no matching mapping."
              >
                {groupSelect(
                  'defaultGroup',
                  groups.filter((g) => !g.isProtected),
                  'authentication-defaultGroup',
                )}
              </SettingsRow>
              {input(
                'groupsClaim',
                'Groups claim',
                'ID token claim that lists the user’s groups or roles, e.g. groups or entitlements.',
              )}
              {input(
                'additionalScopes',
                'Additional scopes',
                'Space-separated. Requested with openid profile email. A provider returns a scope mapping’s claims only for a requested scope.',
              )}
            </SettingsRows>
          )}
          {values.jitEnabled && (
            <>
              <SettingsSubhead>Group mapping</SettingsSubhead>
              <SettingsHint>
                Applied at every sign-in. A user whose Groups claim lists the
                value gets the OpenFarm group. If you remove a mapping, OpenFarm
                removes its group from users at their next sign-in. Groups
                assigned under Users &amp; groups do not change.
              </SettingsHint>
              <div className="mt-2 flex flex-col gap-2">
                {mappings.fields.map((row, index) => {
                  const rowError = errors.groupMappings?.[index]?.externalGroup;
                  return (
                    <div
                      key={row.id}
                      className="grid grid-cols-[minmax(0,1fr)_minmax(0,1fr)_auto] items-start gap-2"
                    >
                      <div>
                        <Input
                          aria-label={`Claim value ${index + 1}`}
                          autoComplete="off"
                          disabled={disabled}
                          aria-invalid={!!rowError}
                          aria-describedby={
                            rowError
                              ? `authentication-groupMappings-${index}-error`
                              : undefined
                          }
                          {...form.register(
                            `groupMappings.${index}.externalGroup`,
                          )}
                        />
                        <FieldError
                          id={`authentication-groupMappings-${index}-error`}
                        >
                          {rowError?.message}
                        </FieldError>
                      </div>
                      <div>
                        {groupSelect(
                          `groupMappings.${index}.group`,
                          groups,
                          `authentication-groupMappings-${index}-group`,
                          `OpenFarm group ${index + 1}`,
                        )}
                      </div>
                      <Button
                        type="button"
                        variant="ghost"
                        size="icon-sm"
                        disabled={disabled}
                        aria-label={`Remove mapping ${index + 1}`}
                        onClick={() => mappings.remove(index)}
                      >
                        <XIcon />
                      </Button>
                    </div>
                  );
                })}
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  className="self-start"
                  disabled={disabled || mappings.fields.length >= 50}
                  onClick={() =>
                    mappings.append({ externalGroup: '', group: '' })
                  }
                >
                  <PlusIcon data-icon="inline-start" />
                  Add mapping
                </Button>
                {mapsProtectedGroup && (
                  <Alert role="status">
                    <AlertDescription>
                      A protected group has full access, including settings.
                      Every user in the mapped provider group gets that access.
                    </AlertDescription>
                  </Alert>
                )}
              </div>
            </>
          )}
        </>
      )}
      {!values.internalAuthenticationEnabled && (
        <>
          <SettingsSubhead>Recovery access</SettingsSubhead>
          <SettingsHint>
            Users must sign in with OIDC. Recovery administrators can still use
            a password. Select administrators with a tested password. Keep one
            available for provider outages. Select your own account when you
            save from a password session. Or sign in with OIDC first.
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
            Changing the provider or client credential ends existing SSO
            sessions. JIT changes apply to new account creation.
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
