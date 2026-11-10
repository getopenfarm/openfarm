import {
  ChevronDownIcon,
  InfoIcon,
  PlusIcon,
  TriangleAlertIcon,
} from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router';
import { Alert, AlertDescription } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from '@/components/ui/collapsible';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { CopyButton } from './CopyButton';
import { assistantClients, type Snippet } from './connectSnippets';

const LOOPBACK = new Set(['localhost', '127.0.0.1', '[::1]']);

/** How to point an AI assistant at this server's MCP endpoint. Own view only. */
export function ConnectAssistantCard({
  mcpUrl,
  signInEnabled,
  defaultOpen,
}: {
  mcpUrl?: string | null;
  signInEnabled: boolean;
  defaultOpen: boolean;
}) {
  const url = mcpUrl ?? `${window.location.origin}/mcp`;
  const parsed = new URL(url);
  const insecure =
    parsed.protocol === 'http:' && !LOOPBACK.has(parsed.hostname);
  const [error, setError] = useState('');
  const clients = assistantClients(url);
  return (
    <Collapsible
      defaultOpen={defaultOpen}
      className="group/connect rounded-lg border bg-card"
    >
      <CollapsibleTrigger className="flex w-full items-center justify-between gap-2 p-4 text-left text-sm font-medium">
        Connect an AI assistant
        <ChevronDownIcon className="size-4 transition-transform group-data-[state=open]/connect:rotate-180" />
      </CollapsibleTrigger>
      <CollapsibleContent className="flex flex-col gap-4 border-t p-4">
        <div className="flex flex-wrap items-center gap-3">
          <span className="text-sm text-muted-foreground">Server URL</span>
          <code className="min-w-0 flex-1 truncate rounded-md border bg-muted px-2 py-1.5 text-sm select-all">
            {url}
          </code>
          <CopyButton
            value={url}
            what="URL"
            aria-label="Copy server URL"
            onError={setError}
          />
        </div>
        {error && (
          <Alert variant="destructive">
            <AlertDescription>{error}</AlertDescription>
          </Alert>
        )}
        {insecure && (
          <Alert>
            <TriangleAlertIcon className="text-kind-amber" />
            <AlertDescription>
              This address is not HTTPS. Cloud assistants cannot reach it, and
              tokens travel unencrypted. Use it only on a trusted network.
            </AlertDescription>
          </Alert>
        )}
        {!signInEnabled && (
          <Alert role="status">
            <InfoIcon />
            <AlertDescription>
              Sign-in is off. Use an access token.
            </AlertDescription>
          </Alert>
        )}
        <Tabs defaultValue={signInEnabled ? 'claude' : 'claude-code'}>
          <TabsList className="flex-wrap">
            {clients.map((client) => (
              <TabsTrigger key={client.id} value={client.id}>
                {client.label}
              </TabsTrigger>
            ))}
          </TabsList>
          {clients.map((client) => (
            <TabsContent
              key={client.id}
              value={client.id}
              className="flex flex-col gap-4 pt-2"
            >
              {signInEnabled && (
                <Way
                  title="Sign in"
                  description="Add the URL, then sign in to OpenFarm when the assistant asks."
                >
                  <Steps steps={client.signIn} onError={setError} />
                </Way>
              )}
              <Way
                title="With an access token"
                description={
                  client.token
                    ? 'Send a token in an Authorization: Bearer header.'
                    : 'Needs sign-in.'
                }
                action={
                  client.token && (
                    <Button asChild variant="outline" size="sm">
                      <Link to="/profile/tokens/new">
                        <PlusIcon data-icon="inline-start" />
                        Add token
                      </Link>
                    </Button>
                  )
                }
              >
                {client.token && (
                  <Steps steps={client.token} onError={setError} />
                )}
              </Way>
            </TabsContent>
          ))}
        </Tabs>
        <p className="flex items-start gap-2 text-xs text-muted-foreground">
          <TriangleAlertIcon className="mt-0.5 size-3.5 shrink-0" />
          The assistant sends what it reads to its AI service. Give it only the
          access it needs.
        </p>
      </CollapsibleContent>
    </Collapsible>
  );
}

function Way({
  title,
  description,
  action,
  children,
}: {
  title: string;
  description: string;
  action?: React.ReactNode;
  children?: React.ReactNode;
}) {
  return (
    <section className="flex flex-col gap-2">
      <div className="flex items-start justify-between gap-3">
        <div>
          <h3 className="text-sm font-medium">{title}</h3>
          <p className="text-sm text-muted-foreground">{description}</p>
        </div>
        {action}
      </div>
      {children}
    </section>
  );
}

function Steps({
  steps,
  onError,
}: {
  steps: Snippet[];
  onError: (message: string) => void;
}) {
  return (
    <ol className="flex flex-col gap-3 text-sm">
      {steps.map((step) => (
        <li key={step.text} className="flex flex-col gap-1.5">
          <p>{step.text}</p>
          {step.file && (
            <p className="font-mono text-xs text-muted-foreground">
              {step.file}
            </p>
          )}
          {step.code && (
            <div className="flex items-start gap-2">
              <pre className="min-w-0 flex-1 overflow-x-auto rounded-md border bg-muted p-3 text-xs">
                <code>{step.code}</code>
              </pre>
              <CopyButton
                value={step.code}
                what="text"
                aria-label={`Copy ${step.file ?? 'snippet'}`}
                onError={onError}
              />
            </div>
          )}
          {step.after && <p className="text-muted-foreground">{step.after}</p>}
        </li>
      ))}
    </ol>
  );
}
