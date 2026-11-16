import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { toast } from 'sonner';
import { PermissionButton as Button } from '@/components/PermissionButton';
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Field, FieldDescription, FieldLabel } from '@/components/ui/field';
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Spinner } from '@/components/ui/spinner';
import { invalidateSlicingChecks } from '@/features/slicing/invalidateSlicingChecks';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import type { PrinterDetailResponse } from '@/lib/api/generated/endpoints.schemas';
import {
  getOpenFarmApiEndpointsPrintersGetPrinterDetailEndpointQueryKey,
  getOpenFarmApiEndpointsPrintersListFleetEndpointQueryKey,
  useOpenFarmApiEndpointsPrintersEditPrinterSlicerSettingsEndpoint,
  useOpenFarmApiEndpointsPrintersGetPrinterDetailEndpoint,
} from '@/lib/api/generated/printers/printers';

export const SLICER_SETTINGS_PERMISSION = 'printers:edit-slicer-settings';

/**
 * Slicer settings of one printer (`null` = closed). Today that is the build plate; any status, since plates are swapped
 * while it prints. It reads the printer detail from the query cache the printer page and the dashboard already fill, so
 * both can open it without passing the printer in.
 */
export function SlicerSettingsDialog({
  printerId,
  onOpenChange,
}: {
  printerId: string | null;
  onOpenChange: (open: boolean) => void;
}) {
  const queryClient = useQueryClient();
  const detail = useOpenFarmApiEndpointsPrintersGetPrinterDetailEndpoint(
    printerId ?? '',
    { query: { enabled: Boolean(printerId) } },
  );
  const printer = detail.data?.data as PrinterDetailResponse | undefined;
  const open = Boolean(printerId && printer);
  const saved = printer?.buildPlateType ?? '';
  const [plate, setPlate] = useState(saved);
  useEffect(() => {
    if (open) setPlate(saved);
  }, [open, saved]);

  const save = useOpenFarmApiEndpointsPrintersEditPrinterSlicerSettingsEndpoint(
    {
      mutation: {
        onSuccess: () => {
          toast.success('Slicer settings saved.');
          invalidateSlicingChecks(queryClient);
          queryClient.invalidateQueries({
            queryKey:
              getOpenFarmApiEndpointsPrintersListFleetEndpointQueryKey(),
          });
          queryClient.invalidateQueries({
            queryKey:
              getOpenFarmApiEndpointsPrintersGetPrinterDetailEndpointQueryKey(
                printerId ?? '',
              ),
          });
          queryClient.invalidateQueries({
            predicate: (query) =>
              String(query.queryKey[0]).endsWith('/activity') ||
              String(query.queryKey[0]).startsWith('/api/v1/audit-log'),
          });
          onOpenChange(false);
        },
        onError: async (error) =>
          toast.error(
            await extractErrorDetail(error, 'Could not save slicer settings.'),
          ),
      },
    },
  );

  const changed = plate !== '' && plate !== saved;
  if (!printer) return null;
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Slicer settings</DialogTitle>
          <DialogDescription>{printer.name}</DialogDescription>
        </DialogHeader>
        <Field className="flex flex-col gap-1.5">
          <FieldLabel htmlFor="build-plate">Build plate</FieldLabel>
          <Select value={plate} onValueChange={setPlate}>
            <SelectTrigger id="build-plate" className="w-full">
              <SelectValue placeholder="Select a plate" />
            </SelectTrigger>
            <SelectContent>
              <SelectGroup>
                {(printer.compatibleBuildPlateTypes ?? []).map((option) => (
                  <SelectItem key={option.value} value={option.value ?? ''}>
                    {option.label}
                  </SelectItem>
                ))}
              </SelectGroup>
            </SelectContent>
          </Select>
          {printer.status === 'Printing' && (
            <FieldDescription>Current print is not affected.</FieldDescription>
          )}
          {changed && saved && (
            <FieldDescription>
              Queued files for {printer.buildPlateTypeLabel} stop matching this
              printer.
            </FieldDescription>
          )}
        </Field>
        <DialogFooter>
          <DialogClose asChild>
            <Button type="button" variant="outline">
              Cancel
            </Button>
          </DialogClose>
          <Button
            permission={SLICER_SETTINGS_PERMISSION}
            disabled={!changed || save.isPending}
            onClick={() =>
              save.mutate({
                id: printerId ?? '',
                data: { buildPlateType: plate },
              })
            }
          >
            {save.isPending && <Spinner data-icon="inline-start" />}
            {save.isPending ? 'Saving…' : 'Save'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
