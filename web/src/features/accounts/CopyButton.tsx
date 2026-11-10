import { CheckIcon, CopyIcon } from 'lucide-react';
import { useEffect, useState } from 'react';
import { Button } from '@/components/ui/button';

/** Copies `value`; shows "Copied" for 2 s. `onError` gets the failure copy. */
export function CopyButton({
  value,
  label = 'Copy',
  what = 'text',
  onError,
  'aria-label': ariaLabel,
}: {
  value: string;
  'aria-label'?: string;
  label?: string;
  /** Names the thing to select manually in the failure message. */
  what?: string;
  onError: (message: string) => void;
}) {
  const [copied, setCopied] = useState(false);
  useEffect(() => {
    if (!copied) return;
    const timer = setTimeout(() => setCopied(false), 2000);
    return () => clearTimeout(timer);
  }, [copied]);
  async function copy() {
    try {
      await navigator.clipboard.writeText(value);
      setCopied(true);
      onError('');
    } catch {
      onError(`Could not copy. Select the ${what} and copy it manually.`);
    }
  }
  return (
    <Button
      type="button"
      variant="outline"
      size="sm"
      aria-label={ariaLabel}
      onClick={() => void copy()}
    >
      {copied ? (
        <CheckIcon data-icon="inline-start" />
      ) : (
        <CopyIcon data-icon="inline-start" />
      )}
      {copied ? 'Copied' : label}
    </Button>
  );
}
