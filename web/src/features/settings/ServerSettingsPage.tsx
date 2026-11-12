import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { Link } from 'react-router';
import { toast } from 'sonner';
import { z } from 'zod';
import { ErrorState } from '@/components/empty-state/EmptyState';
import { Alert, AlertDescription } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
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
import { useUnsavedChangesPrompt } from '@/hooks/useUnsavedChangesPrompt';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import type { ServerSettingsResponse } from '@/lib/api/generated/endpoints.schemas';
import { httpClient } from '@/lib/api/httpClient';
import { useAuthorizedQuery } from '@/lib/auth/useAuthorizedQuery';
import { SettingsRow, SettingsRows, SettingsSection } from './SettingsParts';

const serverKey = '/api/v1/admin/settings/server';
const mcpKey = '/api/v1/admin/settings/mcp';
const modes = ['Disabled', 'AccessTokens', 'SignIn'] as const;
const modeLabels = {
  Disabled: 'Disabled',
  AccessTokens: 'Access tokens only',
  SignIn: 'Sign-in and access tokens',
};

export function ServerSettingsPage() {
  return <InstallationSettings kind="server" />;
}
export function AiAssistantsSettingsPage() {
  return <InstallationSettings kind="mcp" />;
}

function InstallationSettings({ kind }: { kind: 'server' | 'mcp' }) {
  const key = kind === 'server' ? serverKey : mcpKey;
  const query = useAuthorizedQuery({
    queryKey: [key],
    queryFn: ({ signal }) =>
      httpClient<{ data: ServerSettingsResponse }>(key, { signal }),
  });
  return (
    <SettingsSection
      title={kind === 'server' ? 'Server' : 'AI assistants'}
      description={
        kind === 'server'
          ? 'The public address of this OpenFarm installation.'
          : 'Choose whether AI assistants can connect to your farm through MCP.'
      }
    >
      {query.data ? (
        <InstallationForm
          key={query.data.data.revision}
          kind={kind}
          data={query.data.data}
        />
      ) : query.isError ? (
        <ErrorState
          title="Could not load server settings"
          onRetry={() => void query.refetch()}
        />
      ) : (
        <Skeleton className="h-40" />
      )}
    </SettingsSection>
  );
}

const schema = z.object({
  publicUrl: z
    .string()
    .max(2048)
    .refine((value) => {
      if (value === '') return true;
      try {
        const url = new URL(value);
        return (
          /^https?:\/\//.test(value) &&
          !/\s|\\|@|\?|#/.test(value) &&
          !url.username &&
          !url.password &&
          url.pathname === '/' &&
          !url.search &&
          !url.hash &&
          !value.endsWith('/..')
        );
      } catch {
        return false;
      }
    }, 'Enter an HTTP or HTTPS URL without a path, query, credentials, or fragment.'),
  mode: z.enum(modes),
  password: z.string().min(1, 'Enter your current password.').max(1024),
});

function InstallationForm({
  kind,
  data,
}: {
  kind: 'server' | 'mcp';
  data: ServerSettingsResponse;
}) {
  const client = useQueryClient();
  const [error, setError] = useState('');
  const managed =
    kind === 'server' ? data.publicUrlManaged : data.mcpModeManaged;
  const initial = {
    publicUrl: data.publicUrl ?? '',
    mode: (data.mcpMode ?? 'Disabled') as (typeof modes)[number],
    password: '',
  };
  const form = useForm({
    resolver: zodResolver(schema),
    defaultValues: initial,
  });
  const { ConfirmLeaveDialog } = useUnsavedChangesPrompt(
    form.formState.isDirty,
  );
  const key = kind === 'server' ? serverKey : mcpKey;
  const save = useMutation({
    mutationFn: (values: z.infer<typeof schema>) =>
      httpClient<{ data: ServerSettingsResponse }>(key, {
        method: 'PUT',
        body: JSON.stringify({
          revision: data.revision,
          password: values.password,
          ...(kind === 'server'
            ? { publicUrl: values.publicUrl || null }
            : { mode: values.mode }),
        }),
      }),
    onSuccess: (result) => {
      form.reset({
        publicUrl: result.data.publicUrl ?? '',
        mode: (result.data.mcpMode ?? 'Disabled') as (typeof modes)[number],
        password: '',
      });
      client.setQueryData([serverKey], result);
      client.setQueryData([mcpKey], result);
      void client.invalidateQueries({ queryKey: ['/api/v1/auth/apps'] });
      toast.success('Server settings saved.');
    },
    onError: async (failure) => {
      setError(
        await extractErrorDetail(
          failure,
          'Could not save settings. Check your password and reload if settings changed.',
        ),
      );
    },
    onSettled: () => form.resetField('password'),
  });
  const changed =
    kind === 'server'
      ? form.watch('publicUrl') !== initial.publicUrl
      : form.watch('mode') !== initial.mode;
  return (
    <form
      className="flex flex-col gap-4"
      onSubmit={form.handleSubmit((values) => {
        if (!save.isPending && !managed) {
          setError('');
          save.mutate(values);
        }
      })}
    >
      {managed && (
        <Alert role="status">
          <AlertDescription>
            {kind === 'server'
              ? 'Public URL is managed by ORIGIN in the deployment.'
              : data.mcpEnabledOverride != null
                ? `MCP access is managed by MCP_ENABLED=${data.mcpEnabledOverride} in the deployment.`
                : 'MCP mode is managed by MCP_MODE in the deployment.'}
          </AlertDescription>
        </Alert>
      )}
      <SettingsRows>
        {kind === 'server' ? (
          <SettingsRow
            label="Public URL"
            htmlFor="public-url"
            wide
            disabled={managed || save.isPending}
            invalid={!!form.formState.errors.publicUrl}
            help={
              <span id="public-url-help">
                For example, https://farm.example.com. Leave blank for local
                token access. HTTPS makes refresh cookies secure, so sign in
                through this address after saving.
              </span>
            }
          >
            <Input
              id="public-url"
              type="text"
              inputMode="url"
              autoComplete="url"
              disabled={managed || save.isPending}
              aria-invalid={!!form.formState.errors.publicUrl}
              aria-describedby="public-url-help public-url-error"
              {...form.register('publicUrl')}
            />
            <FieldError id="public-url-error">
              {form.formState.errors.publicUrl?.message}
            </FieldError>
          </SettingsRow>
        ) : (
          <SettingsRow
            label="MCP access"
            htmlFor="mcp-mode"
            wide
            disabled={managed || save.isPending}
            help={
              <span id="mcp-mode-help">
                Disabled blocks every MCP connection. Sign-in needs an HTTPS{' '}
                <Link className="underline" to="/settings/server">
                  public URL
                </Link>
                . Access tokens work with compatible local clients.
              </span>
            }
          >
            <Controller
              control={form.control}
              name="mode"
              render={({ field }) => (
                <Select
                  value={field.value}
                  onValueChange={field.onChange}
                  disabled={managed || save.isPending}
                >
                  <SelectTrigger
                    id="mcp-mode"
                    ref={field.ref}
                    className="w-full"
                    aria-describedby="mcp-mode-help"
                  >
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectGroup>
                      {modes.map((mode) => (
                        <SelectItem key={mode} value={mode}>
                          {modeLabels[mode]}
                        </SelectItem>
                      ))}
                    </SelectGroup>
                  </SelectContent>
                </Select>
              )}
            />
          </SettingsRow>
        )}
        {!managed && (
          <SettingsRow
            label="Current password"
            htmlFor="server-password"
            wide
            disabled={save.isPending}
            invalid={!!form.formState.errors.password}
            help={
              <span id="server-password-help">
                Confirm your administrator password to save this change.
              </span>
            }
          >
            <Input
              id="server-password"
              type="password"
              autoComplete="current-password"
              disabled={save.isPending}
              aria-invalid={!!form.formState.errors.password}
              aria-describedby="server-password-help server-password-error"
              {...form.register('password')}
            />
            <FieldError id="server-password-error">
              {form.formState.errors.password?.message}
            </FieldError>
          </SettingsRow>
        )}
      </SettingsRows>
      <p className="text-sm text-muted-foreground">
        Changes apply without restarting OpenFarm. Changing the URL or MCP mode
        disconnects signed-in apps and cancels pending connection requests.
        Personal access tokens remain available for REST integrations.
      </p>
      {kind === 'server' && (
        <p className="text-sm text-muted-foreground">
          Set up DNS, HTTPS, and your reverse proxy separately. Saving a URL
          does not make this server reachable from the internet.
        </p>
      )}
      {kind === 'mcp' && (
        <p className="text-sm">
          <Link className="underline" to="/profile?tab=apps">
            Connect an AI assistant
          </Link>{' '}
          after enabling MCP. Each user chooses the access their assistant
          receives.
        </p>
      )}
      {error && (
        <Alert variant="destructive" role="alert">
          <AlertDescription>{error}</AlertDescription>
        </Alert>
      )}
      {!managed && (
        <div className="flex flex-wrap gap-2">
          <Button type="submit" disabled={!changed || save.isPending}>
            {save.isPending && <Spinner data-icon="inline-start" />}
            {save.isPending ? 'Saving…' : 'Save'}
          </Button>
          <Button
            type="button"
            variant="outline"
            disabled={save.isPending}
            onClick={() => {
              form.reset(initial);
              setError('');
            }}
          >
            Reset changes
          </Button>
        </div>
      )}
      <ConfirmLeaveDialog />
    </form>
  );
}
