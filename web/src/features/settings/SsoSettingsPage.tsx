import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { Controller, useFieldArray, useForm } from 'react-hook-form';
import { Link } from 'react-router';
import { toast } from 'sonner';
import { z } from 'zod';
import { ErrorState } from '@/components/empty-state/EmptyState';
import { Alert, AlertDescription } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Field, FieldError, FieldLabel } from '@/components/ui/field';
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
import type { SsoSettingsResponse } from '@/lib/api/generated/endpoints.schemas';
import { httpClient } from '@/lib/api/httpClient';
import { useAuthorizedQuery } from '@/lib/auth/useAuthorizedQuery';
import {
  SettingsHint,
  SettingsRow,
  SettingsRows,
  SettingsSection,
  SettingsSubhead,
} from './SettingsParts';

const key = '/api/v1/admin/settings/sso';
const schema = z
  .object({
    enabled: z.boolean(),
    required: z.boolean(),
    displayName: z.string().trim().min(1).max(80),
    authority: z.string().trim().max(2048),
    clientId: z.string().trim().max(256),
    clientCredential: z.string().max(4096),
    sessionHours: z.number().int().min(1).max(24),
    recoveryUserIds: z.array(z.string()),
    jitEnabled: z.boolean(),
    requireVerifiedEmail: z.boolean(),
    scimEnabled: z.boolean(),
    scimCredential: z.string().max(512),
    scimRequestsPerMinute: z.number().int().min(1).max(10000),
    defaultGroup: z.string().min(1),
    groupMappings: z
      .array(
        z.object({
          externalGroup: z.string().trim().min(1).max(256),
          localGroup: z.string().min(1),
        }),
      )
      .max(100),
  })
  .superRefine((value, ctx) => {
    if (value.enabled) {
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
    if (value.required && !value.recoveryUserIds.length)
      ctx.addIssue({
        code: 'custom',
        path: ['recoveryUserIds'],
        message: 'Select at least one local recovery administrator.',
      });
    const names = value.groupMappings.map((m) => m.externalGroup.toLowerCase());
    if (new Set(names).size !== names.length)
      ctx.addIssue({
        code: 'custom',
        path: ['groupMappings'],
        message: 'External group names must be unique.',
      });
  });
type Values = z.infer<typeof schema>;
function defaults(data: SsoSettingsResponse): Values {
  return {
    enabled: data.enabled ?? false,
    required: data.required ?? false,
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
    scimEnabled: data.scimEnabled ?? false,
    scimCredential: '',
    scimRequestsPerMinute: data.scimRequestsPerMinute ?? 300,
    defaultGroup: data.defaultGroup ?? 'Viewers',
    groupMappings: (data.groupMappings ?? []).map((m) => ({
      externalGroup: m.externalGroup ?? '',
      localGroup: m.localGroup ?? '',
    })),
  };
}

export function SsoSettingsPage() {
  const query = useAuthorizedQuery({
    queryKey: [key],
    queryFn: ({ signal }) =>
      httpClient<{ data: SsoSettingsResponse }>(key, { signal }),
  });
  return (
    <SettingsSection
      title="Single sign-on"
      description="Connect an identity provider and manage account provisioning."
    >
      {query.data ? (
        <SsoForm key={query.data.data.revision} data={query.data.data} />
      ) : query.isError ? (
        <ErrorState
          title="Could not load single sign-on settings"
          onRetry={() => void query.refetch()}
        />
      ) : (
        <Skeleton className="h-40" />
      )}
    </SettingsSection>
  );
}

function SsoForm({ data }: { data: SsoSettingsResponse }) {
  const client = useQueryClient();
  const [error, setError] = useState('');
  const form = useForm<Values>({
    resolver: zodResolver(
      schema.superRefine((draft, ctx) => {
        if (
          draft.enabled &&
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
        if (
          draft.scimEnabled &&
          !draft.scimCredential &&
          !data.scimCredentialConfigured
        )
          ctx.addIssue({
            code: 'custom',
            path: ['scimCredential'],
            message: 'Enter or generate a provisioning credential.',
          });
        if (
          draft.scimEnabled &&
          draft.scimCredential &&
          (draft.scimCredential.length < 32 ||
            /[\s.]/.test(draft.scimCredential))
        )
          ctx.addIssue({
            code: 'custom',
            path: ['scimCredential'],
            message:
              'Use a credential of at least 32 characters without whitespace or dots.',
          });
      }),
    ),
    defaultValues: defaults(data),
  });
  const mappings = useFieldArray({
    control: form.control,
    name: 'groupMappings',
  });
  const { ConfirmLeaveDialog } = useUnsavedChangesPrompt(
    form.formState.isDirty,
  );
  const values = form.watch();
  const save = useMutation({
    mutationFn: (draft: Values) =>
      httpClient<{ data: SsoSettingsResponse }>(key, {
        method: 'PUT',
        body: JSON.stringify({
          revision: data.revision,
          settings: {
            enabled: draft.enabled,
            required: draft.required,
            displayName: draft.displayName,
            authority: draft.authority || null,
            clientId: draft.clientId || null,
            clientSecret: draft.clientCredential || null,
            sessionHours: draft.sessionHours,
            recoveryUserIds: draft.recoveryUserIds,
            jitEnabled: draft.jitEnabled,
            requireVerifiedEmail: draft.requireVerifiedEmail,
            scimEnabled: draft.scimEnabled,
            scimToken: draft.scimCredential || null,
            scimRequestsPerMinute: draft.scimRequestsPerMinute,
            defaultGroup: draft.defaultGroup,
            groupMappings: Object.fromEntries(
              draft.groupMappings.map((m) => [m.externalGroup, m.localGroup]),
            ),
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
          'Cannot save single sign-on settings. Reload the page and try again.',
        ),
      ),
  });
  const disabled = save.isPending || !!data.recoveryMode;
  const errors = form.formState.errors;
  function toggle(
    name:
      | 'enabled'
      | 'required'
      | 'jitEnabled'
      | 'requireVerifiedEmail'
      | 'scimEnabled',
    label: string,
    help: string,
    unavailable = false,
  ) {
    return (
      <SettingsRow
        label={label}
        htmlFor={`sso-${name}`}
        help={<span id={`sso-${name}-help`}>{help}</span>}
        disabled={disabled || unavailable}
      >
        <Controller
          control={form.control}
          name={name}
          render={({ field }) => (
            <Switch
              id={`sso-${name}`}
              ref={field.ref}
              checked={field.value}
              disabled={disabled || unavailable}
              aria-describedby={`sso-${name}-help`}
              onCheckedChange={(checked) => {
                field.onChange(checked);
                if (name === 'enabled' && !checked)
                  for (const dependent of [
                    'required',
                    'jitEnabled',
                    'scimEnabled',
                  ] as const)
                    form.setValue(dependent, false, { shouldDirty: true });
              }}
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
      | 'sessionHours'
      | 'scimCredential'
      | 'scimRequestsPerMinute',
    label: string,
    help?: string,
    type = 'text',
  ) {
    return (
      <SettingsRow
        label={label}
        htmlFor={`sso-${name}`}
        wide
        disabled={disabled}
        invalid={!!errors[name]}
        help={help ? <span id={`sso-${name}-help`}>{help}</span> : undefined}
      >
        <Input
          id={`sso-${name}`}
          type={type}
          autoComplete={type === 'password' ? 'new-password' : 'off'}
          disabled={disabled}
          min={type === 'number' ? 1 : undefined}
          max={
            name === 'sessionHours'
              ? 24
              : name === 'scimRequestsPerMinute'
                ? 10000
                : undefined
          }
          aria-invalid={!!errors[name]}
          aria-describedby={`sso-${name}-help sso-${name}-error`}
          {...form.register(name, { valueAsNumber: type === 'number' })}
        />
        <FieldError id={`sso-${name}-error`}>
          {errors[name]?.message}
        </FieldError>
      </SettingsRow>
    );
  }
  function groupSelect(
    name: 'defaultGroup' | `groupMappings.${number}.localGroup`,
    id: string,
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
            single sign-on settings.
          </AlertDescription>
        </Alert>
      )}
      {!data.callbackUrl && (
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
      <SettingsRows>
        {toggle(
          'enabled',
          'Single sign-on',
          'Allow users to sign in through OpenID Connect.',
        )}
        {input('displayName', 'Provider name', 'Shown on the sign-in button.')}
        {input(
          'authority',
          'Issuer URL',
          'For authentik, use the per-application issuer URL, including its trailing slash.',
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
      {data.callbackUrl && (
        <SettingsRows>
          <SettingsRow label="Redirect URI" wide>
            <code className="break-all text-xs">{data.callbackUrl}</code>
          </SettingsRow>
          <SettingsRow
            label="Back-channel logout URI"
            wide
            help="Register this address with the provider to end OpenFarm sessions centrally."
          >
            <code className="break-all text-xs">{data.logoutUrl}</code>
          </SettingsRow>
        </SettingsRows>
      )}
      <SettingsSubhead>Login policy</SettingsSubhead>
      <SettingsRows>
        {toggle(
          'required',
          'Require single sign-on',
          'Only selected recovery administrators can use local passwords.',
          !values.enabled,
        )}
      </SettingsRows>
      {values.required && (
        <>
          <SettingsHint>
            Select local administrators whose passwords you have tested. Keep
            one available if the provider is unavailable.
          </SettingsHint>
          <SettingsRows>
            {(data.recoveryAccounts ?? []).map((account) => (
              <SettingsRow
                key={account.id}
                label={account.username}
                htmlFor={`recovery-${account.id}`}
                disabled={disabled}
              >
                <Checkbox
                  id={`recovery-${account.id}`}
                  checked={values.recoveryUserIds.includes(account.id ?? '')}
                  disabled={disabled}
                  aria-invalid={!!errors.recoveryUserIds}
                  aria-describedby={
                    errors.recoveryUserIds ? 'sso-recovery-error' : undefined
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
          <FieldError id="sso-recovery-error">
            {errors.recoveryUserIds?.message}
          </FieldError>
        </>
      )}
      <SettingsSubhead>Account provisioning</SettingsSubhead>
      <SettingsRows>
        {toggle(
          'jitEnabled',
          'Create accounts at first sign-in',
          'Create passwordless accounts for new users allowed by the provider.',
          !values.enabled,
        )}
        {toggle(
          'requireVerifiedEmail',
          'Require verified email',
          'Require email_verified=true when creating an account at first sign-in.',
          !values.jitEnabled,
        )}
        {toggle(
          'scimEnabled',
          'SCIM provisioning',
          'Let the provider create, update, disable, and restore managed accounts.',
          !values.enabled,
        )}
        {(values.jitEnabled || values.scimEnabled) && (
          <SettingsRow
            label="Default group"
            htmlFor="sso-defaultGroup"
            wide
            help="Initial access for new accounts. Administrator groups are excluded."
          >
            {groupSelect('defaultGroup', 'sso-defaultGroup')}
          </SettingsRow>
        )}
      </SettingsRows>
      {values.scimEnabled && (
        <>
          <SettingsRows>
            {input(
              'scimCredential',
              'Provisioning credential',
              data.scimCredentialConfigured
                ? 'Configured. Leave blank to keep it. Enter a new credential to rotate it.'
                : 'Generate a separate credential and copy it into the provider.',
              'password',
            )}
            <SettingsRow label="Provisioning credential actions" wide>
              <div className="flex flex-wrap gap-2">
                <Button
                  type="button"
                  variant="outline"
                  disabled={disabled}
                  onClick={() =>
                    form.setValue(
                      'scimCredential',
                      Array.from(
                        crypto.getRandomValues(new Uint8Array(32)),
                        (b) => b.toString(16).padStart(2, '0'),
                      ).join(''),
                      { shouldDirty: true },
                    )
                  }
                >
                  Generate credential
                </Button>
                <Button
                  type="button"
                  variant="outline"
                  disabled={disabled || !values.scimCredential}
                  onClick={() => {
                    void navigator.clipboard
                      .writeText(values.scimCredential)
                      .then(
                        () => toast.success('Credential copied.'),
                        () => toast.error('Could not copy credential.'),
                      );
                  }}
                >
                  Copy new credential
                </Button>
              </div>
            </SettingsRow>
            {input(
              'scimRequestsPerMinute',
              'Requests per minute',
              'Maximum provisioning requests accepted each minute.',
              'number',
            )}
            {data.scimUrl && (
              <SettingsRow label="SCIM base URL" wide>
                <code className="break-all text-xs">{data.scimUrl}</code>
              </SettingsRow>
            )}
          </SettingsRows>
          <SettingsSubhead>SCIM group mappings</SettingsSubhead>
          <SettingsHint>
            Map provider groups to existing OpenFarm groups. Unmapped groups
            grant no permissions. Local grants remain independent.
          </SettingsHint>
          {!!mappings.fields.length && (
            <SettingsRows>
              {mappings.fields.map((mapping, i) => (
                <SettingsRow
                  key={mapping.id}
                  label={`Group mapping ${i + 1}`}
                  wide
                  disabled={disabled}
                >
                  <div className="flex flex-col gap-2">
                    <Field>
                      <FieldLabel htmlFor={`external-group-${i}`}>
                        External group
                      </FieldLabel>
                      <Input
                        id={`external-group-${i}`}
                        disabled={disabled}
                        aria-invalid={
                          !!errors.groupMappings?.[i]?.externalGroup
                        }
                        aria-describedby={`external-group-${i}-error`}
                        {...form.register(`groupMappings.${i}.externalGroup`)}
                      />
                      <FieldError id={`external-group-${i}-error`}>
                        {errors.groupMappings?.[i]?.externalGroup?.message}
                      </FieldError>
                    </Field>
                    <Field>
                      <FieldLabel htmlFor={`local-group-${i}`}>
                        OpenFarm group
                      </FieldLabel>
                      {groupSelect(
                        `groupMappings.${i}.localGroup`,
                        `local-group-${i}`,
                      )}
                    </Field>
                    <Button
                      type="button"
                      variant="outline"
                      disabled={disabled}
                      onClick={() => mappings.remove(i)}
                      aria-label={`Remove group mapping ${i + 1}`}
                    >
                      Remove mapping
                    </Button>
                  </div>
                </SettingsRow>
              ))}
            </SettingsRows>
          )}
          <FieldError>
            {errors.groupMappings?.message ??
              errors.groupMappings?.root?.message}
          </FieldError>
          <div>
            <Button
              type="button"
              variant="outline"
              disabled={disabled || mappings.fields.length >= 100}
              onClick={() =>
                mappings.append({
                  externalGroup: '',
                  localGroup: values.defaultGroup,
                })
              }
            >
              Add group mapping
            </Button>
          </div>
        </>
      )}
      <SettingsHint>
        Changes apply immediately. Changing the provider, client credential, or
        session duration ends existing SSO sessions. Provisioning changes take
        effect on the provider's next synchronization.
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
