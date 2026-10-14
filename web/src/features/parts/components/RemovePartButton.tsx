import { Trash2Icon } from 'lucide-react';
import { useRef, useState } from 'react';
import { Button } from '@/components/ui/button';
import {
  type PartRemovalNoticeData,
  PartRemovalToast,
} from './PartRemovalToast';
import { RemovePartsDialog } from './RemovePartsDialog';

interface RemovePartButtonProps {
  partId?: string;
  partName?: string;
  onRemoved?: () => void;
  size?: 'sm' | 'default';
  onNotice?: (notice: PartRemovalNoticeData | undefined) => void;
}

export function RemovePartButton({
  partId,
  partName,
  onRemoved,
  size = 'default',
  onNotice,
}: RemovePartButtonProps) {
  const buttonRef = useRef<HTMLButtonElement>(null);
  const [open, setOpen] = useState(false);
  const [notice, setNotice] = useState<PartRemovalNoticeData>();
  const [pending, setPending] = useState(false);
  return (
    <>
      <Button
        ref={buttonRef}
        type="button"
        variant="destructive"
        size={size}
        disabled={!partId || pending}
        onClick={(event) => {
          event.stopPropagation();
          setOpen(true);
        }}
      >
        <Trash2Icon data-icon="inline-start" /> Remove
      </Button>
      {!onNotice && <PartRemovalToast notice={notice} />}
      {open && (
        <RemovePartsDialog
          onNotice={onNotice ?? setNotice}
          parts={partId ? [{ id: partId, name: partName ?? 'this part' }] : []}
          returnFocusRef={buttonRef}
          open={open}
          onOpenChange={setOpen}
          onPendingChange={setPending}
          onRemoved={() => onRemoved?.()}
        />
      )}
    </>
  );
}
