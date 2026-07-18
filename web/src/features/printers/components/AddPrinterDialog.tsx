import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
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
import type { PrinterCatalogResponse } from '@/lib/api/generated/endpoints.schemas';
import {
  getOpenFarmApiEndpointsPrintersListFleetEndpointQueryKey,
  useOpenFarmApiEndpointsPrintersGetPrinterCatalogEndpoint,
  useOpenFarmApiEndpointsPrintersRegisterPrinterEndpoint,
  useOpenFarmApiEndpointsPrintersVerifyPrinterConnectionEndpoint,
} from '@/lib/api/generated/printers/printers';

// FR-007/FR-008 (US3): brand/model options come from the backend catalog, not a hardcoded list —
// adding a brand/model to PrinterModelCatalog now requires zero frontend code change. A compile-time
// z.enum can no longer describe a server-driven list, so brand/modelCode are validated via a
// runtime .refine() closed over the fetched catalog (research.md §5) instead.
const schema = z.object({
  name: z.string().min(1, 'Name is required'),
  brand: z.string().min(1, 'Brand is required'),
  modelCode: z.string().min(1, 'Model is required'),
  ipAddress: z.string().min(1, 'IP address is required'),
  serialNumber: z.string().min(1, 'Serial number is required'),
  accessCode: z.string().min(1, 'Access code is required'),
});

type FormValues = z.infer<typeof schema>;

export function AddPrinterDialog() {
  const [open, setOpen] = useState(false);
  const [verified, setVerified] = useState(false);
  const queryClient = useQueryClient();

  const catalogQuery =
    useOpenFarmApiEndpointsPrintersGetPrinterCatalogEndpoint();
  const catalog = catalogQuery.data?.data as PrinterCatalogResponse | undefined;
  const catalogRef = useRef<PrinterCatalogResponse | undefined>(catalog);
  useEffect(() => {
    catalogRef.current = catalog;
  }, [catalog]);

  const validatedSchema = schema
    .refine(
      (values) =>
        (catalogRef.current?.brands ?? []).some(
          (b) => b.brand === values.brand,
        ),
      { message: 'Unknown brand', path: ['brand'] },
    )
    .refine(
      (values) =>
        (catalogRef.current?.brands ?? [])
          .find((b) => b.brand === values.brand)
          ?.models?.includes(values.modelCode) ?? false,
      { message: 'Unknown model for this brand', path: ['modelCode'] },
    );

  const form = useForm<FormValues>({
    resolver: zodResolver(validatedSchema),
    defaultValues: {
      name: '',
      brand: '',
      modelCode: '',
      ipAddress: '',
      serialNumber: '',
      accessCode: '',
    },
  });

  // Seed brand/model with the catalog's first entries once it arrives, if the operator hasn't
  // already picked something (e.g. the dialog was opened before the catalog finished loading).
  useEffect(() => {
    if (!catalog?.brands?.length) {
      return;
    }
    if (!form.getValues('brand')) {
      const firstBrand = catalog.brands[0];
      form.setValue('brand', firstBrand.brand ?? '');
      form.setValue('modelCode', firstBrand.models?.[0] ?? '');
    }
  }, [catalog, form]);

  const selectedBrand = form.watch('brand');
  const modelsForBrand =
    catalog?.brands?.find((b) => b.brand === selectedBrand)?.models ?? [];

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
    const { brand, modelCode, ipAddress, serialNumber, accessCode } =
      form.getValues();
    setVerified(false);
    if (!ipAddress || !serialNumber || !accessCode) {
      return;
    }
    verifyMutation.mutate({
      data: { brand, modelCode, ipAddress, serialNumber, accessCode },
    });
  }

  // Brand/Model determine which connector a printer routes to, so switching either invalidates
  // any prior verify result exactly like a connection-field edit does.
  function handleBrandChange(newBrand: string) {
    form.setValue('brand', newBrand);
    const models = catalog?.brands?.find((b) => b.brand === newBrand)?.models;
    form.setValue('modelCode', models?.[0] ?? '');
    handleConnectionFieldBlur();
  }

  function handleModelChange(newModelCode: string) {
    form.setValue('modelCode', newModelCode);
    handleConnectionFieldBlur();
  }

  function onSubmit(values: FormValues) {
    registerMutation.mutate({ data: values });
  }

  const catalogUnavailable = catalogQuery.isError;

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
              name="brand"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Brand</FormLabel>
                  <FormControl>
                    <select
                      className="h-9 w-full rounded-md border bg-transparent px-3 text-sm"
                      disabled={catalogQuery.isLoading || catalogUnavailable}
                      {...field}
                      onChange={(e) => handleBrandChange(e.target.value)}
                    >
                      {(catalog?.brands ?? []).map((b) => (
                        <option key={b.brand} value={b.brand}>
                          {b.brand}
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
              name="modelCode"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Model</FormLabel>
                  <FormControl>
                    <select
                      className="h-9 w-full rounded-md border bg-transparent px-3 text-sm"
                      disabled={catalogQuery.isLoading || catalogUnavailable}
                      {...field}
                      onChange={(e) => handleModelChange(e.target.value)}
                    >
                      {modelsForBrand.map((model) => (
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
            {catalogUnavailable && (
              <p className="text-sm text-destructive">
                Couldn't load the printer catalog. Try again before adding a
                printer.
              </p>
            )}
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
                disabled={
                  !verified || registerMutation.isPending || catalogUnavailable
                }
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
