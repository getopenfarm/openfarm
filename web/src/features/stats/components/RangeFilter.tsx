import { Input } from '@/components/ui/input';
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group';
import { type DaySpan, lastDays, toInstants } from '../lib/range';

/** The last 7, 30 or 90 days (`preset`), or the `custom` days when `preset` is 'custom'. */
export interface RangeValue {
  preset: string;
  custom: DaySpan;
}

export function defaultRange(): RangeValue {
  return { preset: '30', custom: lastDays(30) };
}

/** The `{ from, to }` the API filters on. */
export function rangeInstants({ preset, custom }: RangeValue) {
  return toInstants(preset === 'custom' ? custom : lastDays(Number(preset)));
}

/** 7 d · 30 d · 90 d · Custom, plus the two dates when Custom is on. */
export function RangeFilter({
  value,
  onChange,
}: {
  value: RangeValue;
  onChange: (value: RangeValue) => void;
}) {
  const { preset, custom } = value;
  return (
    <>
      <ToggleGroup
        aria-label="Range"
        type="single"
        variant="outline"
        value={preset}
        onValueChange={(next) => {
          if (next) onChange({ ...value, preset: next });
        }}
      >
        <ToggleGroupItem value="7">7 d</ToggleGroupItem>
        <ToggleGroupItem value="30">30 d</ToggleGroupItem>
        <ToggleGroupItem value="90">90 d</ToggleGroupItem>
        <ToggleGroupItem value="custom">Custom</ToggleGroupItem>
      </ToggleGroup>
      {preset === 'custom' && (
        <div className="flex items-center gap-2">
          <Input
            type="date"
            aria-label="From"
            className="w-auto"
            value={custom.start}
            max={custom.end}
            onChange={(event) => {
              const start = event.target.value;
              if (start) onChange({ ...value, custom: { ...custom, start } });
            }}
          />
          <span className="text-muted-foreground">–</span>
          <Input
            type="date"
            aria-label="To"
            className="w-auto"
            value={custom.end}
            min={custom.start}
            onChange={(event) => {
              const end = event.target.value;
              if (end) onChange({ ...value, custom: { ...custom, end } });
            }}
          />
        </div>
      )}
    </>
  );
}
