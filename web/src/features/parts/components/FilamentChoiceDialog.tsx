import { ChevronRightIcon, SearchIcon } from 'lucide-react';
import { useId, useState } from 'react';
import { Link } from 'react-router';
import { Swatch } from '@/components/swatch/Swatch';
import { Button } from '@/components/ui/button';
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from '@/components/ui/collapsible';
import {
  DialogClose,
  DialogDescription,
  DialogScrollableContent,
  DialogTitle,
} from '@/components/ui/dialog';
import {
  InputGroup,
  InputGroupAddon,
  InputGroupInput,
} from '@/components/ui/input-group';
import { matchesEveryWord } from '@/features/spools/components/SpoolLabel';
import type { FilamentResponse } from '@/lib/api/generated/endpoints.schemas';
import { cn } from '@/lib/utils';

/**
 * A requirement's choice: one color, any color of one filament, any color of the material (`anyColor`), or none —
 * the file's color in any brand. At most one is set.
 */
export interface FilamentChoice {
  variantId: string | null;
  filamentId: string | null;
  anyColor: boolean;
}

export const MATCH_FILE_COLOR: FilamentChoice = {
  variantId: null,
  filamentId: null,
  anyColor: false,
};

export const hex = (value: string | null | undefined) =>
  value ? `#${value.replace(/^#/, '').toUpperCase()}` : undefined;

export const filamentName = (filament: FilamentResponse) =>
  [filament.vendorName, filament.material].filter(Boolean).join(' ');

const ANY_COLOR = 'Any color';

/** "Any PETG", or "Any material" when the file names none. */
export const materialScope = (materialType: string | null | undefined) =>
  materialType ? `Any ${materialType}` : 'Any material';

// Every choice is named "<scope> · <color>", as the API's FilamentChoiceLabel names it.
/** "Any PETG · #000000", or "Any PETG · Any color". */
export const genericLabel = (
  materialType: string | null | undefined,
  colorHex: string | null | undefined,
  anyColor = false,
) =>
  `${materialScope(materialType)} · ${anyColor || !colorHex ? ANY_COLOR : hex(colorHex)}`;

/** "Aliz PETG Basic · Black", or "Aliz PETG Basic · Any color". */
export const catalogLabel = (
  filament: FilamentResponse,
  colorName?: string | null,
) => `${filamentName(filament)} · ${colorName ?? ANY_COLOR}`;

const FILE = 'file';
const ANY = 'any';
const toValue = (choice: FilamentChoice) =>
  choice.variantId
    ? `variant:${choice.variantId}`
    : choice.filamentId
      ? `filament:${choice.filamentId}`
      : choice.anyColor
        ? ANY
        : FILE;
const fromValue = (value: string): FilamentChoice => {
  const [kind, id] = value.split(':');
  return kind === 'variant'
    ? { ...MATCH_FILE_COLOR, variantId: id }
    : kind === 'filament'
      ? { ...MATCH_FILE_COLOR, filamentId: id }
      : { ...MATCH_FILE_COLOR, anyColor: kind === ANY };
};

const collator = new Intl.Collator(undefined, { sensitivity: 'base' });

const optionClass =
  'flex cursor-pointer items-center gap-2 rounded-md px-2 py-1.5 text-sm hover:bg-accent/40 has-checked:bg-accent has-checked:font-medium has-focus-visible:outline-2 has-focus-visible:-outline-offset-2 has-focus-visible:outline-ring';

interface FilamentChoiceDialogProps {
  /** The requirement as the file states it, for the header. */
  requirementLabel: string;
  materialType: string | null | undefined;
  detectedColorHex: string | null | undefined;
  filaments: FilamentResponse[];
  value: FilamentChoice;
  onValueChange: (choice: FilamentChoice) => void;
}

/**
 * The dialog body: mount it inside a `Dialog` so each open starts from `value`. Lists the catalog filaments of the
 * requirement's material; each takes any of its colors, or one color picked from its list. Done commits the choice.
 */
export function FilamentChoiceDialog({
  requirementLabel,
  materialType,
  detectedColorHex,
  filaments,
  value,
  onValueChange,
}: FilamentChoiceDialogProps) {
  const name = useId();
  const [draft, setDraft] = useState(toValue(value));
  const [search, setSearch] = useState('');
  const [expanded, setExpanded] = useState<Set<string>>(
    () =>
      new Set(
        filaments
          .filter((f) => f.variants?.some((v) => v.id === value.variantId))
          .map((f) => f.id ?? ''),
      ),
  );

  const groups = filaments
    .filter((f) => !materialType || f.materialType === materialType)
    .sort((a, b) => collator.compare(filamentName(a), filamentName(b)))
    .map((filament) => {
      const nameMatches = matchesEveryWord(filamentName(filament), search);
      const colors = [...(filament.variants ?? [])]
        .sort((a, b) => collator.compare(a.colorName ?? '', b.colorName ?? ''))
        .filter(
          (variant) =>
            nameMatches ||
            matchesEveryWord(
              `${filamentName(filament)} ${variant.colorName}`,
              search,
            ),
        );
      return { filament, colors, visible: nameMatches || colors.length > 0 };
    })
    .filter((group) => group.visible);

  function toggle(id: string, open: boolean) {
    setExpanded((prev) => {
      const next = new Set(prev);
      if (open) next.add(id);
      else next.delete(id);
      return next;
    });
  }

  return (
    <DialogScrollableContent
      className="sm:max-w-lg"
      header={
        <>
          <DialogTitle>Production filament</DialogTitle>
          <DialogDescription className="flex items-center gap-2">
            <Swatch colorHex={hex(detectedColorHex)} size="sm" />
            {requirementLabel}
            {detectedColorHex && (
              <span className="font-mono">{hex(detectedColorHex)}</span>
            )}
          </DialogDescription>
        </>
      }
      footer={
        <>
          <DialogClose asChild>
            <Button type="button" variant="outline">
              Cancel
            </Button>
          </DialogClose>
          <DialogClose asChild>
            <Button
              type="button"
              onClick={() => {
                // Unchanged: keep the caller's value, which may hold a profile choice this dialog doesn't show.
                if (draft !== toValue(value)) onValueChange(fromValue(draft));
              }}
            >
              Done
            </Button>
          </DialogClose>
        </>
      }
    >
      <div className="flex flex-col gap-3">
        <InputGroup>
          <InputGroupAddon>
            <SearchIcon />
          </InputGroupAddon>
          <InputGroupInput
            type="search"
            placeholder="Search filament or color"
            aria-label="Search filament or color"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
          />
        </InputGroup>
        <fieldset className="m-0 flex min-w-0 flex-col gap-1 border-0 p-0">
          <legend className="sr-only">Production filament</legend>
          {/* The material in any brand, laid out like a filament: the file's color, or any color. */}
          <div className="rounded-md border">
            <div className="flex items-center gap-1 pr-1">
              <span className="flex min-w-0 flex-1 items-center gap-2 px-2 py-2 text-sm font-medium">
                <span aria-hidden className="size-4 shrink-0" />
                <span className="truncate">{materialScope(materialType)}</span>
              </span>
              {detectedColorHex && (
                <label className={cn(optionClass, 'shrink-0')}>
                  <input
                    type="radio"
                    name={name}
                    className="sr-only"
                    checked={draft === ANY}
                    onChange={() => setDraft(ANY)}
                    aria-label={genericLabel(materialType, null, true)}
                  />
                  <Swatch auto />
                  {ANY_COLOR}
                </label>
              )}
            </div>
            <div className="flex flex-col gap-0.5 border-t p-1">
              <label className={optionClass}>
                <input
                  type="radio"
                  name={name}
                  className="sr-only"
                  checked={draft === FILE}
                  onChange={() => setDraft(FILE)}
                  aria-label={genericLabel(materialType, detectedColorHex)}
                />
                <Swatch
                  colorHex={hex(detectedColorHex)}
                  auto={!detectedColorHex}
                />
                {detectedColorHex ? (
                  <span className="font-mono">{hex(detectedColorHex)}</span>
                ) : (
                  ANY_COLOR
                )}
              </label>
            </div>
          </div>
          {groups.map(({ filament, colors }) => {
            const id = filament.id ?? '';
            const open = search.trim() !== '' || expanded.has(id);
            return (
              <Collapsible
                key={id}
                open={open}
                onOpenChange={(next) => toggle(id, next)}
                className="rounded-md border"
              >
                <div className="flex items-center gap-1 pr-1">
                  <CollapsibleTrigger asChild>
                    <button
                      type="button"
                      className="flex min-w-0 flex-1 items-center gap-2 rounded-md px-2 py-2 text-left text-sm font-medium outline-none focus-visible:ring-3 focus-visible:ring-ring/50"
                    >
                      <ChevronRightIcon
                        aria-hidden
                        className={cn(
                          'size-4 shrink-0 text-muted-foreground transition-transform',
                          open && 'rotate-90',
                        )}
                      />
                      <span className="truncate">{filamentName(filament)}</span>
                      <span className="shrink-0 text-xs font-normal text-muted-foreground tabular-nums">
                        {filament.variants?.length ?? 0} colors
                      </span>
                    </button>
                  </CollapsibleTrigger>
                  <label className={cn(optionClass, 'shrink-0')}>
                    <input
                      type="radio"
                      name={name}
                      className="sr-only"
                      checked={draft === `filament:${id}`}
                      onChange={() => setDraft(`filament:${id}`)}
                      aria-label={catalogLabel(filament)}
                    />
                    <Swatch auto />
                    {ANY_COLOR}
                  </label>
                </div>
                <CollapsibleContent className="flex flex-col gap-0.5 border-t p-1">
                  {colors.map((variant) => (
                    <label key={variant.id} className={optionClass}>
                      <input
                        type="radio"
                        name={name}
                        className="sr-only"
                        checked={draft === `variant:${variant.id}`}
                        onChange={() => setDraft(`variant:${variant.id}`)}
                        aria-label={catalogLabel(filament, variant.colorName)}
                      />
                      <Swatch colorHex={hex(variant.colorHex)} />
                      {variant.colorName}
                    </label>
                  ))}
                </CollapsibleContent>
              </Collapsible>
            );
          })}
          {groups.length === 0 && (
            <p className="px-2 py-3 text-sm text-muted-foreground">
              {filaments.length === 0 ? (
                <>
                  No filament yet.{' '}
                  <Link
                    to="/filament/new"
                    className="font-medium text-primary underline-offset-4 hover:underline"
                  >
                    Add filament
                  </Link>
                </>
              ) : search.trim() ? (
                'No filament matches.'
              ) : (
                `No ${materialType ?? ''} filament in the catalog.`
              )}
            </p>
          )}
        </fieldset>
      </div>
    </DialogScrollableContent>
  );
}
