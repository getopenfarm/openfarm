import { KeyRoundIcon } from 'lucide-react';
import { useState } from 'react';
import { Alert, AlertDescription } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { CopyButton } from './CopyButton';

/** Mounted only for the immediate create/regenerate response. */
export function TokenCredential({
  credential,
  onDone,
}: {
  credential: string;
  onDone: () => void;
}) {
  const [error, setError] = useState('');
  return (
    <section
      className="flex flex-col gap-3 rounded-lg border border-primary bg-card p-4"
      aria-label="Token credential"
    >
      <p
        className="flex items-center gap-2 text-sm font-semibold"
        role="status"
      >
        <KeyRoundIcon className="size-4" />
        Copy the credential now. It won’t be shown again.
      </p>
      <div className="flex flex-wrap items-center gap-3">
        <code className="min-w-0 flex-1 rounded-md border bg-muted p-3 text-sm break-all select-all">
          {credential}
        </code>
        <CopyButton value={credential} what="credential" onError={setError} />
      </div>
      <p className="text-xs text-muted-foreground">
        Send it in an <code>Authorization: Bearer</code> header. If you lose it,
        regenerate it from the list.
      </p>
      {error && (
        <Alert variant="destructive">
          <AlertDescription>{error}</AlertDescription>
        </Alert>
      )}
      <Button type="button" className="self-start" onClick={onDone}>
        Done
      </Button>
    </section>
  );
}
