import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { useForm } from 'react-hook-form';
import { toast } from 'sonner';
import { z } from 'zod';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from '@/components/ui/form';
import { Input } from '@/components/ui/input';
import { ProfilePickerCombobox } from '@/features/profiles/components/ProfilePickerCombobox';
import {
  getOpenFarmApiEndpointsPrintersListFleetEndpointQueryKey,
  useOpenFarmApiEndpointsPrintersEditPrinterEndpoint,
  useOpenFarmApiEndpointsPrintersVerifyPrinterConnectionEndpoint,
} from '@/lib/api/generated/printers/printers';

const schema = z.object({
  name: z.string().min(1, 'Name is required'),
  ipAddress: z.string(),
  accessCode: z.string(),
});

type FormValues = z.infer<typeof schema>;

export interface EditPrinterDialogProps {
  printerId: string;
  printerName: string;
  brand: string;
  modelCode: string;
  serialNumber: string;
  currentIpAddress: string;
  machineProfileId: string | null | undefined;
  machineProfileName: string | null | undefined;
  processProfileId: string | null | undefined;
  processProfileName: string | null | undefined;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function EditPrinterDialog({
  printerId,
  printerName,
  brand,
  modelCode,
  serialNumber,
  currentIpAddress,
  machineProfileId,
  machineProfileName,
  processProfileId,
  processProfileName,
  open,
  onOpenChange,
}: EditPrinterDialogProps) {
  const [verified, setVerified] = useState(false);
  const queryClient = useQueryClient();

  const [selectedMachineProfileId, setSelectedMachineProfileId] = useState(
    machineProfileId ?? null,
  );
  const [selectedMachineProfileName, setSelectedMachineProfileName] =
    useState(machineProfileName ?? null);
  const [selectedProcessProfileId, setSelectedProcessProfileId] = useState(
    processProfileId ?? null,
  );
  const [selectedProcessProfileName, setSelectedProcessProfileName] =
    useState(processProfileName ?? null);

  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      name: printerName,
      ipAddress: currentIpAddress,
      accessCode: '',
    },
  });

  // Re-seed the form (and reset verification) whenever a different printer's dialog opens.
  useEffect(() => {
    if (open) {
      form.reset({
        name: printerName,
        ipAddress: currentIpAddress,
        accessCode: '',
      });
      setVerified(false);
      setSelectedMachineProfileId(machineProfileId ?? null);
      setSelectedMachineProfileName(machineProfileName ?? null);
      setSelectedProcessProfileId(processProfileId ?? null);
      setSelectedProcessProfileName(processProfileName ?? null);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, printerName, currentIpAddress, machineProfileId, processProfileId]);

  const ipAddress = form.watch('ipAddress');
  const accessCode = form.watch('accessCode');
  // research.md §7: verification only gates Save when the operator has actually touched
  // connection fields — a name-only change never triggers the verify-before-save flow (FR-003).
  const connectionChanged =
    ipAddress !== currentIpAddress || accessCode.length > 0;

  const verifyMutation =
    useOpenFarmApiEndpointsPrintersVerifyPrinterConnectionEndpoint({
      mutation: {
        onSuccess: () => setVerified(true),
        onError: () => setVerified(false),
      },
    });

  const editMutation = useOpenFarmApiEndpointsPrintersEditPrinterEndpoint({
    mutation: {
      onSuccess: () => {
        toast.success('Printer updated.');
        setVerified(false);
        onOpenChange(false);
        queryClient.invalidateQueries({
          queryKey: getOpenFarmApiEndpointsPrintersListFleetEndpointQueryKey(),
        });
      },
      onError: () => {
        toast.error('Could not update printer.');
      },
    },
  });

  // FR-002: same verify-before-save gate as registration, but only when connection fields changed.
  function handleConnectionFieldBlur() {
    const values = form.getValues();
    setVerified(false);
    if (
      values.ipAddress === currentIpAddress &&
      values.accessCode.length === 0
    ) {
      return;
    }
    if (!values.ipAddress || !values.accessCode) {
      return;
    }
    verifyMutation.mutate({
      data: {
        ipAddress: values.ipAddress,
        serialNumber,
        accessCode: values.accessCode,
      },
    });
  }

  function handleMachineProfileChange(id: string | null) {
    setSelectedMachineProfileId(id);
    setSelectedMachineProfileName(null);
    // FR-007: any machine profile change (clear or switch to a different one) clears the process
    // profile too — a process selection can never outlive the exact machine it was validated
    // against, so it always needs re-picking once the machine changes.
    setSelectedProcessProfileId(null);
    setSelectedProcessProfileName(null);
  }

  function handleProcessProfileChange(id: string | null) {
    setSelectedProcessProfileId(id);
    setSelectedProcessProfileName(null);
  }

  function onSubmit(values: FormValues) {
    editMutation.mutate({
      id: printerId,
      data: connectionChanged
        ? {
            name: values.name,
            ipAddress: values.ipAddress,
            accessCode: values.accessCode,
            machineProfileId: selectedMachineProfileId,
            processProfileId: selectedProcessProfileId,
          }
        : {
            name: values.name,
            machineProfileId: selectedMachineProfileId,
            processProfileId: selectedProcessProfileId,
          },
    });
  }

  const saveDisabled = connectionChanged
    ? !verified || editMutation.isPending
    : editMutation.isPending;

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Edit {printerName}</DialogTitle>
          <DialogDescription>
            {brand} {modelCode} — connection changes stay gated on verification;
            renaming alone saves immediately.
          </DialogDescription>
        </DialogHeader>
        <Form {...form}>
          <form
            onSubmit={form.handleSubmit(onSubmit)}
            className="flex flex-col gap-4"
          >
            <FormField
              control={form.control}
              name="name"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Name</FormLabel>
                  <FormControl>
                    <Input {...field} />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
            <FormField
              control={form.control}
              name="ipAddress"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>IP address</FormLabel>
                  <FormControl>
                    <Input
                      {...field}
                      onBlur={() => {
                        field.onBlur();
                        handleConnectionFieldBlur();
                      }}
                    />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
            <FormField
              control={form.control}
              name="accessCode"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>LAN access code</FormLabel>
                  <FormControl>
                    <Input
                      type="password"
                      placeholder="Leave blank to keep the current connection"
                      {...field}
                      onBlur={() => {
                        field.onBlur();
                        handleConnectionFieldBlur();
                      }}
                    />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
            {connectionChanged && verifyMutation.isPending && (
              <p className="text-sm text-muted-foreground">
                Verifying connection…
              </p>
            )}
            {connectionChanged &&
              verifyMutation.isError &&
              !verifyMutation.isPending && (
                <p className="text-sm text-destructive">
                  Could not verify this printer's connection. Check the IP
                  address and LAN access code.
                </p>
              )}
            {connectionChanged && verified && !verifyMutation.isPending && (
              <p className="text-sm text-status-complete">
                Connection verified.
              </p>
            )}
            <div className="flex flex-col gap-1.5">
              <span className="text-sm font-medium">Machine profile</span>
              <ProfilePickerCombobox
                kind="machine"
                value={selectedMachineProfileId}
                onChange={handleMachineProfileChange}
                selectedLabel={selectedMachineProfileName}
                placeholder="No machine profile linked"
              />
            </div>
            <div className="flex flex-col gap-1.5">
              <span className="text-sm font-medium">Process profile</span>
              <ProfilePickerCombobox
                kind="process"
                value={selectedProcessProfileId}
                onChange={handleProcessProfileChange}
                selectedLabel={selectedProcessProfileName}
                placeholder="No process profile linked"
                disabled={!selectedMachineProfileId}
                disabledPlaceholder="Select a machine profile first"
                printerId={printerId}
              />
            </div>
            <DialogFooter>
              <Button type="submit" disabled={saveDisabled}>
                {editMutation.isPending ? 'Saving…' : 'Save'}
              </Button>
            </DialogFooter>
          </form>
        </Form>
      </DialogContent>
    </Dialog>
  );
}
