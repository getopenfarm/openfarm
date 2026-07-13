import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
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
  DialogTrigger,
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
  useOpenFarmApiEndpointsPrintersRegisterPrinterEndpoint,
  useOpenFarmApiEndpointsPrintersVerifyPrinterConnectionEndpoint,
} from '@/lib/api/generated/printers/printers';

const PRINTER_MODELS = [
  'X1',
  'X1C',
  'X1E',
  'P1P',
  'P1S',
  'A1',
  'A1MINI',
] as const;

const schema = z.object({
  name: z.string().min(1, 'Name is required'),
  modelCode: z.enum(PRINTER_MODELS),
  ipAddress: z.string().min(1, 'IP address is required'),
  serialNumber: z.string().min(1, 'Serial number is required'),
  accessCode: z.string().min(1, 'Access code is required'),
});

type FormValues = z.infer<typeof schema>;

export function AddPrinterDialog() {
  const [open, setOpen] = useState(false);
  const [verified, setVerified] = useState(false);
  const queryClient = useQueryClient();

  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      name: '',
      modelCode: 'P1S',
      ipAddress: '',
      serialNumber: '',
      accessCode: '',
    },
  });

  const verifyMutation =
    useOpenFarmApiEndpointsPrintersVerifyPrinterConnectionEndpoint({
      mutation: {
        onSuccess: () => setVerified(true),
        onError: () => setVerified(false),
      },
    });

  const registerMutation =
    useOpenFarmApiEndpointsPrintersRegisterPrinterEndpoint({
      mutation: {
        onSuccess: () => {
          toast.success('Printer added.');
          form.reset();
          setVerified(false);
          setOpen(false);
          queryClient.invalidateQueries({
            queryKey:
              getOpenFarmApiEndpointsPrintersListFleetEndpointQueryKey(),
          });
        },
        onError: () => {
          toast.error('Could not add printer.');
        },
      },
    });

  // FR-001a: verify-before-add — re-checked on every connection-field blur; submit stays
  // disabled until a fresh verify succeeds. Any subsequent edit invalidates the prior result.
  function handleConnectionFieldBlur() {
    const { ipAddress, serialNumber, accessCode } = form.getValues();
    setVerified(false);
    if (!ipAddress || !serialNumber || !accessCode) {
      return;
    }
    verifyMutation.mutate({ data: { ipAddress, serialNumber, accessCode } });
  }

  function onSubmit(values: FormValues) {
    // Bambu is the only supported brand this phase (spec.md Assumptions) — not a user-editable field.
    registerMutation.mutate({ data: { ...values, brand: 'Bambu' } });
  }

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        <Button>Add printer</Button>
      </DialogTrigger>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Add printer</DialogTitle>
          <DialogDescription>
            Connecting directly over LAN disconnects this printer from Bambu
            Cloud and the official app while OpenFarm is connected.
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
              name="modelCode"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Model</FormLabel>
                  <FormControl>
                    <select
                      className="h-9 w-full rounded-md border bg-transparent px-3 text-sm"
                      {...field}
                    >
                      {PRINTER_MODELS.map((model) => (
                        <option key={model} value={model}>
                          {model}
                        </option>
                      ))}
                    </select>
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
              name="serialNumber"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Serial number</FormLabel>
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
                disabled={!verified || registerMutation.isPending}
              >
                {registerMutation.isPending ? 'Adding…' : 'Add printer'}
              </Button>
            </DialogFooter>
          </form>
        </Form>
      </DialogContent>
    </Dialog>
  );
}
