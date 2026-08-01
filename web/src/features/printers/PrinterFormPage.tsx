import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import { useForm } from 'react-hook-form';
import { useNavigate, useParams } from 'react-router';
import { toast } from 'sonner';
import { z } from 'zod';
import { BackLink } from '@/components/back-link/BackLink';
import { FormFooter } from '@/components/form-footer/FormFooter';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';
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
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { ProfilePickerCombobox } from '@/features/profiles/components/ProfilePickerCombobox';
import { useUnsavedChangesPrompt } from '@/hooks/useUnsavedChangesPrompt';
import type {
  BuildPlateTypeOptionResponse,
  PrinterCatalogResponse,
  PrinterDetailResponse,
} from '@/lib/api/generated/endpoints.schemas';
import {
  getOpenFarmApiEndpointsPrintersGetPrinterDetailEndpointQueryKey,
  getOpenFarmApiEndpointsPrintersListFleetEndpointQueryKey,
  useOpenFarmApiEndpointsPrintersEditPrinterEndpoint,
  useOpenFarmApiEndpointsPrintersGetPrinterCatalogEndpoint,
  useOpenFarmApiEndpointsPrintersGetPrinterDetailEndpoint,
  useOpenFarmApiEndpointsPrintersRegisterPrinterEndpoint,
  useOpenFarmApiEndpointsPrintersVerifyPrinterConnectionEndpoint,
} from '@/lib/api/generated/printers/printers';

interface PrinterFormPageProps {
  mode: 'add' | 'edit';
}

export function PrinterFormPage({ mode }: PrinterFormPageProps) {
  if (mode === 'add') {
    return <AddPrinterForm />;
  }
  return <EditPrinterForm />;
}

// FR-007/FR-008 (US3): brand/model options come from the backend catalog, not a hardcoded list —
// adding a brand/model to PrinterModelCatalog now requires zero frontend code change. A compile-time
// z.enum can no longer describe a server-driven list, so brand/modelCode are validated via a
// runtime .refine() closed over the fetched catalog (research.md §5) instead.
const addSchema = z.object({
  name: z.string().min(1, 'Name is required'),
  brand: z.string().min(1, 'Brand is required'),
  modelCode: z.string().min(1, 'Model is required'),
  ipAddress: z.string().min(1, 'IP address is required'),
  serialNumber: z.string().min(1, 'Serial number is required'),
  accessCode: z.string().min(1, 'Access code is required'),
});

type AddFormValues = z.infer<typeof addSchema>;

function AddPrinterForm() {
  const navigate = useNavigate();
  const [verified, setVerified] = useState(false);
  const queryClient = useQueryClient();

  const catalogQuery =
    useOpenFarmApiEndpointsPrintersGetPrinterCatalogEndpoint();
  const catalog = catalogQuery.data?.data as PrinterCatalogResponse | undefined;
  const catalogRef = useRef<PrinterCatalogResponse | undefined>(catalog);
  useEffect(() => {
    catalogRef.current = catalog;
  }, [catalog]);

  const validatedSchema = addSchema
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

  const form = useForm<AddFormValues>({
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

  const { ConfirmLeaveDialog, markClean } = useUnsavedChangesPrompt(
    form.formState.isDirty,
  );

  // Seed brand/model with the catalog's first entries once it arrives, if the operator hasn't
  // already picked something (e.g. the page was opened before the catalog finished loading).
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
          queryClient.invalidateQueries({
            queryKey:
              getOpenFarmApiEndpointsPrintersListFleetEndpointQueryKey(),
          });
          markClean();
          navigate('/printers');
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

  function onSubmit(values: AddFormValues) {
    registerMutation.mutate({ data: values });
  }

  const catalogUnavailable = catalogQuery.isError;

  return (
    <div className="flex flex-col gap-6">
      <BackLink to="/printers">Back to fleet</BackLink>

      <PageHeader
        title="Add printer"
        description="Connecting directly over LAN disconnects this printer from Bambu Cloud and the official app while OpenFarm is connected."
      />

      <Form {...form}>
        <form
          onSubmit={form.handleSubmit(onSubmit)}
          className="flex flex-col gap-6"
        >
          <Card>
            <CardContent className="grid grid-cols-1 gap-4 sm:grid-cols-2">
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
                <p className="text-sm text-destructive sm:col-span-2">
                  Couldn't load the printer catalog. Try again before adding a
                  printer.
                </p>
              )}
              {verifyMutation.isPending && (
                <p className="text-sm text-muted-foreground sm:col-span-2">
                  Verifying connection…
                </p>
              )}
              {verifyMutation.isError && !verifyMutation.isPending && (
                <p className="text-sm text-destructive sm:col-span-2">
                  Could not verify this printer's connection. Check the IP
                  address and LAN access code.
                </p>
              )}
              {verified && !verifyMutation.isPending && (
                <p className="text-sm text-status-complete sm:col-span-2">
                  Connection verified.
                </p>
              )}
            </CardContent>
          </Card>

          <FormFooter
            start={
              <Button
                type="button"
                variant="ghost"
                onClick={() => navigate('/printers')}
              >
                Cancel
              </Button>
            }
            end={
              <Button
                type="submit"
                disabled={
                  !verified || registerMutation.isPending || catalogUnavailable
                }
              >
                {registerMutation.isPending ? 'Adding…' : 'Add printer'}
              </Button>
            }
          />
        </form>
      </Form>

      <ConfirmLeaveDialog />
    </div>
  );
}

const editSchema = z.object({
  name: z.string().min(1, 'Name is required'),
  ipAddress: z.string(),
  accessCode: z.string(),
});

type EditFormValues = z.infer<typeof editSchema>;

function EditPrinterForm() {
  const { id } = useParams<{ id: string }>();
  const detailQuery = useOpenFarmApiEndpointsPrintersGetPrinterDetailEndpoint(
    id ?? '',
  );
  const printer = detailQuery.data?.data as PrinterDetailResponse | undefined;

  if (detailQuery.isLoading) {
    return (
      <div className="flex flex-col gap-6">
        <BackLink to="/printers">Back to fleet</BackLink>
      </div>
    );
  }

  if (!printer) {
    return (
      <div className="flex flex-col gap-6">
        <BackLink to="/printers">Back to fleet</BackLink>
        <p className="text-sm text-muted-foreground">Printer not found.</p>
      </div>
    );
  }

  return (
    <EditPrinterFormLoaded
      printerId={printer.id ?? id ?? ''}
      printerName={printer.name ?? ''}
      brand={printer.brand ?? ''}
      modelCode={printer.modelCode ?? ''}
      serialNumber={printer.serialNumber ?? ''}
      currentIpAddress={printer.ipAddress ?? ''}
      machineProfileId={printer.machineProfileId}
      machineProfileName={printer.machineProfileName}
      processProfileId={printer.processProfileId}
      processProfileName={printer.processProfileName}
      buildPlateType={printer.buildPlateType}
      compatibleBuildPlateTypes={printer.compatibleBuildPlateTypes}
    />
  );
}

interface EditPrinterFormLoadedProps {
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
  buildPlateType: string | null | undefined;
  compatibleBuildPlateTypes: BuildPlateTypeOptionResponse[] | undefined;
}

// 012 US1 — sentinel for Radix Select, which rejects an empty-string item value; matches the
// "__any__" pattern PartFilamentRequirementsList already uses for its own clearable Select.
const NO_BUILD_PLATE_VALUE = '__none__';

function EditPrinterFormLoaded({
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
  buildPlateType,
  compatibleBuildPlateTypes,
}: EditPrinterFormLoadedProps) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [verified, setVerified] = useState(false);

  const [selectedMachineProfileId, setSelectedMachineProfileId] = useState(
    machineProfileId ?? null,
  );
  // undefined (distinct from null) tells ProfilePickerCombobox to resolve the display name itself
  // via a GET-by-id lookup — used right after a fresh pick, when this component doesn't yet know
  // the newly-selected item's name.
  const [selectedMachineProfileName, setSelectedMachineProfileName] = useState<
    string | null | undefined
  >(machineProfileName);
  const [selectedProcessProfileId, setSelectedProcessProfileId] = useState(
    processProfileId ?? null,
  );
  const [selectedProcessProfileName, setSelectedProcessProfileName] = useState<
    string | null | undefined
  >(processProfileName);
  const [selectedBuildPlateType, setSelectedBuildPlateType] = useState(
    buildPlateType ?? null,
  );

  const form = useForm<EditFormValues>({
    resolver: zodResolver(editSchema),
    defaultValues: {
      name: printerName,
      ipAddress: currentIpAddress,
      accessCode: '',
    },
  });

  const { ConfirmLeaveDialog, markClean } = useUnsavedChangesPrompt(
    form.formState.isDirty,
  );

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
        queryClient.invalidateQueries({
          queryKey: getOpenFarmApiEndpointsPrintersListFleetEndpointQueryKey(),
        });
        queryClient.invalidateQueries({
          queryKey:
            getOpenFarmApiEndpointsPrintersGetPrinterDetailEndpointQueryKey(
              printerId,
            ),
        });
        // RecentActivityCard fetches org-wide and filters client-side to this printer — no
        // printer-scoped params to key off, so invalidate the endpoint's URL prefix broadly.
        queryClient.invalidateQueries({
          queryKey: ['/api/v1/activity-log'],
        });
        markClean();
        navigate('/printers');
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

  function handleMachineProfileChange(id: string | null, name?: string | null) {
    setSelectedMachineProfileId(id);
    setSelectedMachineProfileName(name ?? null);
    // FR-007: any machine profile change (clear or switch to a different one) clears the process
    // profile too — a process selection can never outlive the exact machine it was validated
    // against, so it always needs re-picking once the machine changes.
    setSelectedProcessProfileId(null);
    setSelectedProcessProfileName(null);
  }

  function handleProcessProfileChange(id: string | null, name?: string | null) {
    setSelectedProcessProfileId(id);
    setSelectedProcessProfileName(name ?? null);
  }

  function onSubmit(values: EditFormValues) {
    editMutation.mutate({
      id: printerId,
      data: connectionChanged
        ? {
            name: values.name,
            ipAddress: values.ipAddress,
            accessCode: values.accessCode,
            machineProfileId: selectedMachineProfileId,
            processProfileId: selectedProcessProfileId,
            buildPlateType: selectedBuildPlateType,
          }
        : {
            name: values.name,
            machineProfileId: selectedMachineProfileId,
            processProfileId: selectedProcessProfileId,
            buildPlateType: selectedBuildPlateType,
          },
    });
  }

  const saveDisabled = connectionChanged
    ? !verified || editMutation.isPending
    : editMutation.isPending;

  return (
    <div className="flex flex-col gap-6">
      <BackLink to="/printers">Back to fleet</BackLink>

      <PageHeader
        title={`Edit ${printerName}`}
        description={`${brand} ${modelCode} — connection changes stay gated on verification; renaming alone saves immediately.`}
      />

      <Form {...form}>
        <form
          onSubmit={form.handleSubmit(onSubmit)}
          className="flex flex-col gap-6"
        >
          <Card>
            <CardContent className="flex flex-col gap-4">
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
                  compatibleWithMachineName={
                    selectedMachineProfileName ?? undefined
                  }
                />
              </div>
              <div className="flex flex-col gap-1.5">
                <span className="text-sm font-medium">Build plate</span>
                <Select
                  value={selectedBuildPlateType ?? NO_BUILD_PLATE_VALUE}
                  onValueChange={(value) =>
                    setSelectedBuildPlateType(
                      value === NO_BUILD_PLATE_VALUE ? null : value,
                    )
                  }
                >
                  <SelectTrigger className="w-full">
                    <SelectValue placeholder="Not selected" />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value={NO_BUILD_PLATE_VALUE}>
                      Not selected
                    </SelectItem>
                    {(compatibleBuildPlateTypes ?? []).map((option) => (
                      <SelectItem key={option.value} value={option.value ?? ''}>
                        {option.label}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
            </CardContent>
          </Card>

          <FormFooter
            start={
              <Button
                type="button"
                variant="ghost"
                onClick={() => navigate('/printers')}
              >
                Cancel
              </Button>
            }
            end={
              <Button type="submit" disabled={saveDisabled}>
                {editMutation.isPending ? 'Saving…' : 'Save'}
              </Button>
            }
          />
        </form>
      </Form>

      <ConfirmLeaveDialog />
    </div>
  );
}
