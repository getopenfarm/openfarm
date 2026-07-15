import { PrinterIcon, XIcon } from 'lucide-react';
import { type KeyboardEvent, useMemo, useState } from 'react';
import { useLocation, useNavigate, useParams } from 'react-router';
import { toast } from 'sonner';
import { CheckerboardThumbnail } from '@/components/checkerboard-thumbnail/CheckerboardThumbnail';
import { FormFooter } from '@/components/form-footer/FormFooter';
import { PrintQueueDialog } from '@/components/print-queue-dialog/PrintQueueDialog';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { formatMinutes } from '@/lib/formatMinutes';
import { MaterialAutoMatchSection } from './components/MaterialAutoMatchSection';
import {
  type DetectedColorSlot,
  type Part,
  partsStore,
  useParts,
} from './data/mockPartsStore';
import { resolveSlotDisplayColor } from './lib/colorMatch';

const SLICERS: Part['slicer'][] = [
  'Bambu Studio',
  'OrcaSlicer',
  'PrusaSlicer',
  'Cura',
];

interface LocationState {
  draftPart?: Part;
}

export function PartEditPage() {
  const { id } = useParams<{ id: string }>();
  const location = useLocation();
  const navigate = useNavigate();
  const [parts] = useParts();

  const existingPart = parts.find((part) => part.id === id);
  const draftPart = (location.state as LocationState | null)?.draftPart;
  const isNew = !existingPart && Boolean(draftPart);
  const sourcePart = existingPart ?? draftPart;

  const [name, setName] = useState(sourcePart?.name ?? '');
  const [slicer, setSlicer] = useState<Part['slicer']>(
    sourcePart?.slicer ?? 'Bambu Studio',
  );
  const [tags, setTags] = useState<string[]>(sourcePart?.tags ?? []);
  const [tagInput, setTagInput] = useState('');
  const [colorSlots, setColorSlots] = useState<DetectedColorSlot[]>(
    sourcePart?.colorSlots ?? [],
  );
  const [printOpen, setPrintOpen] = useState(false);

  const printQueuePart = useMemo(() => {
    if (!sourcePart) return null;
    const primarySlot = colorSlots[0];
    return {
      name: name.trim() || sourcePart.name,
      estimatedTimeMinutes: sourcePart.estimatedTimeMinutes,
      weightGrams: sourcePart.weightGrams,
      primaryMaterial: primarySlot
        ? {
            colorHex: resolveSlotDisplayColor(primarySlot),
            label: primarySlot.materialType,
          }
        : undefined,
    };
  }, [sourcePart, name, colorSlots]);

  if (!sourcePart) {
    return (
      <div className="flex flex-col items-center gap-2 rounded-xl border border-dashed p-12 text-center">
        <p className="text-sm font-medium">Part not found</p>
        <p className="text-sm text-muted-foreground">
          It may have been removed, or the link is out of date.
        </p>
        <Button variant="outline" onClick={() => navigate('/parts')}>
          Back to library
        </Button>
      </div>
    );
  }

  // Rebind to a non-optional `Part` — closures below (e.g. handleSave)
  // capture this by reference, and TS can't carry the `if (!sourcePart)`
  // narrowing above across a nested function declaration.
  const part: Part = sourcePart;

  function addTag() {
    const trimmed = tagInput.trim();
    if (!trimmed) return;
    setTags((prev) => (prev.includes(trimmed) ? prev : [...prev, trimmed]));
    setTagInput('');
  }

  function removeTag(tag: string) {
    setTags((prev) => prev.filter((t) => t !== tag));
  }

  function handleTagKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === 'Enter') {
      event.preventDefault();
      addTag();
    } else if (
      event.key === 'Backspace' &&
      tagInput === '' &&
      tags.length > 0
    ) {
      setTags((prev) => prev.slice(0, -1));
    }
  }

  function handleSave() {
    const trimmedName = name.trim();
    if (!trimmedName) {
      toast.error('Give this part a name first.');
      return;
    }

    const savedPart: Part = {
      ...part,
      name: trimmedName,
      slicer,
      tags,
      colorSlots,
    };

    if (isNew) {
      partsStore.setState((prev) => [...prev, savedPart]);
      toast.success('Part created.');
    } else {
      partsStore.setState((prev) =>
        prev.map((existing) =>
          existing.id === savedPart.id ? savedPart : existing,
        ),
      );
      toast.success('Part updated.');
    }
    navigate('/parts');
  }

  return (
    <div className="flex flex-col gap-6 pb-4">
      <div>
        <h2 className="font-heading text-2xl font-semibold">
          {isNew ? 'New part' : 'Edit part'}
        </h2>
        <p className="text-sm text-muted-foreground">
          {isNew
            ? 'Review what OpenFarm detected before adding it to your library.'
            : 'Update this part’s details and material matching.'}
        </p>
      </div>

      <div className="flex items-center gap-4 rounded-xl border p-4">
        <CheckerboardThumbnail
          label={part.fileFormat}
          className="size-16 shrink-0 rounded-md"
        />
        <div className="grid flex-1 grid-cols-2 gap-x-6 gap-y-2 sm:grid-cols-4">
          <div className="flex flex-col">
            <span className="text-xs text-muted-foreground">Est. time</span>
            <span className="font-mono text-sm">
              {formatMinutes(part.estimatedTimeMinutes)}
            </span>
          </div>
          <div className="flex flex-col">
            <span className="text-xs text-muted-foreground">Weight</span>
            <span className="font-mono text-sm">{part.weightGrams} g</span>
          </div>
          <div className="flex flex-col">
            <span className="text-xs text-muted-foreground">Revision</span>
            <span className="font-mono text-sm">v{part.revision}</span>
          </div>
          <div className="flex flex-col">
            <span className="text-xs text-muted-foreground">Format</span>
            <span className="font-mono text-sm">{part.fileFormat}</span>
          </div>
        </div>
      </div>

      <div className="flex flex-col gap-1.5">
        <Label htmlFor="part-name">Part name</Label>
        <Input
          id="part-name"
          value={name}
          onChange={(event) => setName(event.target.value)}
        />
      </div>

      <div className="flex flex-col gap-1.5">
        <Label>Slicer</Label>
        <Select
          value={slicer}
          onValueChange={(value) => setSlicer(value as Part['slicer'])}
        >
          <SelectTrigger className="w-full">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {SLICERS.map((option) => (
              <SelectItem key={option} value={option}>
                {option}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      <div className="flex flex-col gap-1.5">
        <Label>Tags</Label>
        <div className="flex flex-wrap items-center gap-1.5 rounded-md border px-2 py-1.5">
          {tags.map((tag) => (
            <span
              key={tag}
              className="inline-flex items-center gap-1 rounded-full bg-muted px-2 py-0.5 text-xs"
            >
              {tag}
              <button
                type="button"
                aria-label={`Remove ${tag}`}
                onClick={() => removeTag(tag)}
              >
                <XIcon className="size-3" />
              </button>
            </span>
          ))}
          <input
            value={tagInput}
            onChange={(event) => setTagInput(event.target.value)}
            onKeyDown={handleTagKeyDown}
            placeholder="Add a tag…"
            className="min-w-24 flex-1 bg-transparent py-0.5 text-sm outline-none"
          />
        </div>
      </div>

      <MaterialAutoMatchSection
        colorSlots={colorSlots}
        onChange={setColorSlots}
      />

      <FormFooter
        start={
          <Button
            type="button"
            variant="outline"
            onClick={() => setPrintOpen(true)}
          >
            <PrinterIcon /> Print
          </Button>
        }
        end={
          <>
            <Button
              type="button"
              variant="ghost"
              onClick={() => navigate('/parts')}
            >
              Cancel
            </Button>
            <Button type="button" onClick={handleSave}>
              {isNew ? 'Create part' : 'Save changes'}
            </Button>
          </>
        }
      />

      {printQueuePart ? (
        <PrintQueueDialog
          open={printOpen}
          onOpenChange={setPrintOpen}
          part={printQueuePart}
        />
      ) : null}
    </div>
  );
}
