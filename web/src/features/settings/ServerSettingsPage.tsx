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
          ? 'Set the public address for OpenFarm.'
          : 'Set access for AI assistants.'
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
    }, 'Enter an HTTP or HTTPS server address.'),
  mode: z.enum(modes),
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
          ...(kind === 'server'
            ? { publicUrl: values.publicUrl || null }
            : { mode: values.mode }),
        }),
      }),
    onSuccess: (result) => {
      form.reset({
        publicUrl: result.data.publicUrl ?? '',
        mode: (result.data.mcpMode ?? 'Disabled') as (typeof modes)[number],
      });
      client.setQueryData([serverKey], result);
      client.setQueryData([mcpKey], result);
      void client.invalidateQueries({ queryKey: ['/api/v1/auth/apps'] });
      toast.success('Settings saved.');
    },
    onError: async (failure) => {
      setError(
        await extractErrorDetail(
          failure,
          'Cannot save settings. Reload the page and try again.',
        ),
      );
    },
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
              ? 'Set by ORIGIN.'
              : data.mcpEnabledOverride != null
                ? `Set by MCP_ENABLED=${data.mcpEnabledOverride}.`
                : 'Set by MCP_MODE.'}
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
                Use this address to sign in. Leave blank for local token access.
              </span>
            }
          >
            <Input
              id="public-url"
              type="text"
              inputMode="url"
              autoComplete="url"
              placeholder="https://farm.example.com"
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
                {form.watch('mode') === 'Disabled' ? (
                  'AI assistants cannot connect.'
                ) : form.watch('mode') === 'AccessTokens' ? (
                  'Use a personal access token to connect.'
                ) : (
                  <>
                    For remote sign-in, set an HTTPS{' '}
                    <Link className="underline" to="/settings/server">
                      public URL
                    </Link>
                    .
                  </>
                )}
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
      </SettingsRows>
      <p className="text-sm text-muted-foreground">
        Changes disconnect signed-in apps. Connect the apps again.
      </p>
      {kind === 'mcp' && data.mcpMode !== 'Disabled' && (
        <p className="text-sm">
          <Link className="underline" to="/profile?tab=apps">
            Connect an AI assistant
          </Link>
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
