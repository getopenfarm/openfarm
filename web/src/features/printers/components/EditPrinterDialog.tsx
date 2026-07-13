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
import {
  getOpenFarmApiEndpointsPrintersListFleetEndpointQueryKey,
  useOpenFarmApiEndpointsPrintersEditPrinterConnectionEndpoint,
  useOpenFarmApiEndpointsPrintersVerifyPrinterConnectionEndpoint,
} from '@/lib/api/generated/printers/printers';

const schema = z.object({
  ipAddress: z.string().min(1, 'IP address is required'),
  accessCode: z.string().min(1, 'Access code is required'),
});

type FormValues = z.infer<typeof schema>;

export interface EditPrinterDialogProps {
  printerId: string;
  printerName: string;
  serialNumber: string;
  currentIpAddress: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function EditPrinterDialog({
  printerId,
  printerName,
  serialNumber,
  currentIpAddress,
  open,
  onOpenChange,
}: EditPrinterDialogProps) {
  const [verified, setVerified] = useState(false);
  const queryClient = useQueryClient();

  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { ipAddress: currentIpAddress, accessCode: '' },
  });

  // Re-seed the form (and reset verification) whenever a different printer's dialog opens.
  useEffect(() => {
    if (open) {
      form.reset({ ipAddress: currentIpAddress, accessCode: '' });
      setVerified(false);
    }
  }, [open, currentIpAddress, form]);

  const verifyMutation =
    useOpenFarmApiEndpointsPrintersVerifyPrinterConnectionEndpoint({
      mutation: {
        onSuccess: () => setVerified(true),
        onError: () => setVerified(false),
      },
    });

  const editMutation =
    useOpenFarmApiEndpointsPrintersEditPrinterConnectionEndpoint({
      mutation: {
        onSuccess: () => {
          toast.success('Printer connection updated.');
          setVerified(false);
          onOpenChange(false);
          queryClient.invalidateQueries({
            queryKey:
              getOpenFarmApiEndpointsPrintersListFleetEndpointQueryKey(),
          });
        },
        onError: () => {
          toast.error('Could not update printer connection.');
        },
      },
    });

  // FR-002: same verify-before-save gate as registration.
  function handleConnectionFieldBlur() {
    const { ipAddress, accessCode } = form.getValues();
    setVerified(false);
    if (!ipAddress || !accessCode) {
      return;
    }
    verifyMutation.mutate({ data: { ipAddress, serialNumber, accessCode } });
  }

  function onSubmit(values: FormValues) {
    editMutation.mutate({ id: printerId, data: values });
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Edit {printerName}'s connection</DialogTitle>
          <DialogDescription>
            Save stays disabled until the new details are verified reachable.
          </DialogDescription>
        </DialogHeader>
        <Form {...form}>
          <form
            onSubmit={form.handleSubmit(onSubmit)}
            className="flex flex-col gap-4"
          >
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
            {verifyMutation.isPending && (
              <p className="text-sm text-muted-foreground">
                Verifying connection…
              </p>
            )}
            {verifyMutation.isError && !verifyMutation.isPending && (
              <p className="text-sm text-destructive">
                Could not verify this printer's connection. Check the IP address
                and LAN access code.
              </p>
            )}
            {verified && !verifyMutation.isPending && (
              <p className="text-sm text-status-complete">
                Connection verified.
              </p>
            )}
            <DialogFooter>
              <Button
                type="submit"
                disabled={!verified || editMutation.isPending}
              >
                {editMutation.isPending ? 'Saving…' : 'Save'}
              </Button>
            </DialogFooter>
          </form>
        </Form>
      </DialogContent>
    </Dialog>
  );
}
