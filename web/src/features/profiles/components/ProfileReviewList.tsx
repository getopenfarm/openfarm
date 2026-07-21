import { Checkbox } from '@/components/ui/checkbox';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import type {
  FilamentPresetPreviewItem,
  MachinePresetPreviewItem,
  ProcessPresetPreviewItem,
} from '@/lib/api/generated/endpoints.schemas';

export type ProfileDuplicateAction = 'Skip' | 'ImportAsNew';

export interface ProfileSelectionState {
  selected: boolean;
  duplicateAction?: ProfileDuplicateAction;
}

export type ProfileSelectionMap = Record<string, ProfileSelectionState>;

export function profileSelectionKey(
  type: 'Machine' | 'Filament' | 'Process',
  name: string,
): string {
  return `${type}:${name}`;
}

interface PresetLike {
  name?: string;
  baseProfileName?: string | null;
  isDuplicate?: boolean;
}

interface ProfileReviewListProps {
  machinePresets: MachinePresetPreviewItem[];
  processPresets: ProcessPresetPreviewItem[];
  filamentPresets: FilamentPresetPreviewItem[];
  selections: ProfileSelectionMap;
  onChange: (key: string, next: ProfileSelectionState) => void;
}

export function ProfileReviewList({
  machinePresets,
  processPresets,
  filamentPresets,
  selections,
  onChange,
}: ProfileReviewListProps) {
  return (
    <div className="flex flex-col gap-4">
      <PresetSection
        title="Machine profiles"
        type="Machine"
        presets={machinePresets}
        selections={selections}
        onChange={onChange}
      />
      <PresetSection
        title="Process profiles"
        type="Process"
        presets={processPresets}
        selections={selections}
        onChange={onChange}
      />
      <PresetSection
        title="Filament profiles"
        type="Filament"
        presets={filamentPresets}
        selections={selections}
        onChange={onChange}
      />
    </div>
  );
}

function PresetSection({
  title,
  type,
  presets,
  selections,
  onChange,
}: {
  title: string;
  type: 'Machine' | 'Filament' | 'Process';
  presets: PresetLike[];
  selections: ProfileSelectionMap;
  onChange: (key: string, next: ProfileSelectionState) => void;
}) {
  if (presets.length === 0) {
    return null;
  }

  return (
    <div>
      <h3 className="mb-2 text-sm font-semibold">{title}</h3>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead className="w-10" />
            <TableHead>Name</TableHead>
            <TableHead>Base / Override</TableHead>
            <TableHead>Duplicate</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {presets.map((preset) => {
            const name = preset.name ?? '';
            const key = profileSelectionKey(type, name);
            const state = selections[key] ?? { selected: true };

            // FR-006 — warn when a selected override's base is present elsewhere in this same
            // bundle but not also selected, computed entirely from data already in hand (the full
            // preview array + live selection state), no backend round trip (research.md §4).
            const baseName = preset.baseProfileName;
            const baseIsInBundle =
              baseName != null && presets.some((p) => p.name === baseName);
            const baseIsSelected =
              baseName != null &&
              (selections[profileSelectionKey(type, baseName)]?.selected ??
                true);
            const showUnselectedBaseWarning =
              state.selected && baseIsInBundle && !baseIsSelected;

            return (
              <TableRow key={key}>
                <TableCell>
                  <Checkbox
                    checked={state.selected}
                    onCheckedChange={(checked) =>
                      onChange(key, { ...state, selected: checked === true })
                    }
                    aria-label={`Select ${name}`}
                  />
                </TableCell>
                <TableCell>{name}</TableCell>
                <TableCell className="text-sm text-muted-foreground">
                  {preset.baseProfileName
                    ? `Override of ${preset.baseProfileName}`
                    : 'Base'}
                  {showUnselectedBaseWarning && (
                    <p className="text-xs text-destructive">
                      Base "{baseName}" is in this bundle but not selected.
                    </p>
                  )}
                </TableCell>
                <TableCell>
                  {preset.isDuplicate ? (
                    <select
                      className="h-8 rounded-md border bg-transparent px-2 text-xs"
                      value={state.duplicateAction ?? 'Skip'}
                      onChange={(e) =>
                        onChange(key, {
                          ...state,
                          duplicateAction: e.target
                            .value as ProfileDuplicateAction,
                        })
                      }
                      aria-label={`Duplicate action for ${name}`}
                    >
                      <option value="Skip">Skip</option>
                      <option value="ImportAsNew">Import as new</option>
                    </select>
                  ) : (
                    <span className="text-xs text-muted-foreground">—</span>
                  )}
                </TableCell>
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
    </div>
  );
}
