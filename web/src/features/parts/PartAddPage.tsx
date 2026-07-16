import { InfoIcon, Loader2Icon, UploadCloudIcon } from 'lucide-react';
import { useState } from 'react';
import { useNavigate } from 'react-router';
import { buttonVariants } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import type { Part } from './data/mockPartsStore';

const DEMO_FILENAME = 'Bracket mount v1.3mf';

function titleCaseFromFilename(filename: string): string {
  const withoutExtension = filename.replace(/\.[^./]+$/, '');
  return withoutExtension
    .split(/\s+/)
    .filter(Boolean)
    .map((word) => word.charAt(0).toUpperCase() + word.slice(1))
    .join(' ');
}

/** Hardcoded demo result — this is a fully simulated parse, there's no real
 * file/3D-slicer parsing happening. Produces a not-yet-saved draft `Part`
 * that PartEditPage receives via router state so nothing is committed to
 * `partsStore` until the user actually saves. */
function buildParsedPart(): Part {
  return {
    id: `part-${crypto.randomUUID()}`,
    name: titleCaseFromFilename(DEMO_FILENAME),
    folderId: null,
    estimatedTimeMinutes: 72,
    weightGrams: 38,
    revision: 1,
    fileFormat: '3MF',
    slicer: 'Bambu Studio',
    tags: [],
    colorSlots: [
      { slotIndex: 0, detectedColorHex: '#18181b', materialType: 'PLA' },
      { slotIndex: 1, detectedColorHex: '#f97316', materialType: 'PLA' },
    ],
  };
}

export function PartAddPage() {
  const navigate = useNavigate();
  const [status, setStatus] = useState<'idle' | 'parsing'>('idle');

  function startUpload() {
    if (status === 'parsing') return;
    setStatus('parsing');
    const delayMs = 600 + Math.round(Math.random() * 300);
    setTimeout(() => {
      const draftPart = buildParsedPart();
      navigate(`/parts/${draftPart.id}/edit`, { state: { draftPart } });
    }, delayMs);
  }

  return (
    <div className="flex flex-col gap-6">
      <div>
        <h2 className="font-heading text-2xl font-semibold">Add part</h2>
        <p className="text-sm text-muted-foreground">
          Upload a sliced or source file — OpenFarm detects its print time,
          weight, and color slots.
        </p>
      </div>

      <button
        type="button"
        aria-label="Upload part file"
        disabled={status === 'parsing'}
        onClick={startUpload}
        className="flex w-full cursor-pointer flex-col items-center gap-3 rounded-xl border-2 border-dashed p-16 text-center transition-colors hover:bg-muted/40 disabled:pointer-events-none"
      >
        {status === 'parsing' ? (
          <>
            <div className="flex size-12 items-center justify-center rounded-xl bg-primary/10">
              <Loader2Icon className="size-6 animate-spin text-primary" />
            </div>
            <p className="font-medium">Parsing your file…</p>
            <p className="text-sm text-muted-foreground">
              Detecting print time, weight, and color slots.
            </p>
          </>
        ) : (
          <>
            <div className="flex size-12 items-center justify-center rounded-xl bg-primary/10">
              <UploadCloudIcon className="size-6 text-primary" />
            </div>
            <p className="font-medium">Drag &amp; drop your files here</p>
            <p className="text-sm text-muted-foreground">or click to browse</p>
            {/* Visual affordance only — the whole dropzone above is already
             * a single <button>, so this is a styled span (not a nested
             * button) that shares the same click handler via bubbling. */}
            <span className={cn(buttonVariants({ variant: 'outline' }))}>
              Browse files
            </span>
            <p className="text-xs text-muted-foreground">
              3MF · STL · STEP · OBJ · GCODE — up to 200 MB
            </p>
          </>
        )}
      </button>

      <div className="flex gap-3 rounded-xl border bg-muted/30 p-4">
        <InfoIcon className="size-5 shrink-0 text-muted-foreground" />
        <p className="text-sm text-muted-foreground">
          <span className="font-medium text-foreground">.3mf</span> files carry
          plate setup, per-part color assignments, and slicer settings in one
          bundle. Raw <span className="font-medium text-foreground">.stl</span>/
          <span className="font-medium text-foreground">.step</span> files are
          geometry only — OpenFarm will ask you to confirm material and color
          slots after upload.
        </p>
      </div>
    </div>
  );
}
