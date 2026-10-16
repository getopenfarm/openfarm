import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import { useForm } from 'react-hook-form';
import { useNavigate, useParams } from 'react-router';
import { toast } from 'sonner';
import { z } from 'zod';
import { BackLink } from '@/components/back-link/BackLink';
import { EmptyState } from '@/components/empty-state/EmptyState';
import { FormFooter } from '@/components/form-footer/FormFooter';
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card';
import { FieldGroup } from '@/components/ui/field';
import {
  Form,
  FormControl,
  FormDescription,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from '@/components/ui/form';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Skeleton } from '@/components/ui/skeleton';
import { Spinner } from '@/components/ui/spinner';
import { ProfilePickerCombobox } from '@/features/profiles/components/ProfilePickerCombobox';
import { useUnsavedChangesPrompt } from '@/hooks/useUnsavedChangesPrompt';
import type {
  BuildPlateTypeOptionResponse,
  DiscoveredPrinterResponse,
  PrinterCatalogResponse,
  PrinterDetailResponse,
} from '@/lib/api/generated/endpoints.schemas';
import {
  getOpenFarmApiEndpointsPrintersGetDiscoveredPrintersEndpointQueryKey,
  getOpenFarmApiEndpointsPrintersGetPrinterDetailEndpointQueryKey,
  getOpenFarmApiEndpointsPrintersListFleetEndpointQueryKey,
  useOpenFarmApiEndpointsPrintersEditPrinterEndpoint,
  useOpenFarmApiEndpointsPrintersGetPrinterCatalogEndpoint,
  useOpenFarmApiEndpointsPrintersGetPrinterDetailEndpoint,
  useOpenFarmApiEndpointsPrintersRegisterPrinterEndpoint,
  useOpenFarmApiEndpointsPrintersVerifyPrinterConnectionEndpoint,
} from '@/lib/api/generated/printers/printers';
import { DiscoverPrinters } from './DiscoverPrinters';

interface PrinterFormPageProps {
  mode: 'add' | 'edit';
}

export function PrinterFormPage({ mode }: PrinterFormPageProps) {
  if (mode === 'add') {
    return <AddPrinterPage />;
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

// Discovery-first: the scan list is the page. A discovered printer only needs a name and its access
// code; manual entry is the fallback for printers discovery can't see (another subnet, LAN mode off).
function AddPrinterPage() {
  const navigate = useNavigate();
  const [step, setStep] = useState<DiscoveredPrinterResponse | 'manual'>();

  return (
    <Page className="flex-1">
      <PageHeader
        above={
          <BackLink to="/printers" className="mb-1">
            Back to fleet
          </BackLink>
        }
        title="Add printer"
      />

      {step === undefined ? (
        <DiscoverPrinters
          onSelect={setStep}
          onManual={() => setStep('manual')}
        />
      ) : (
        <>
          <Button
            variant="link"
            className="self-start px-0"
            onClick={() => setStep(undefined)}
          >
            ← Back
          </Button>
          {step === 'manual' ? (
            <AddPrinterForm onAdded={() => navigate('/printers')} />
          ) : (
            // Back to the scan list so the next printer is one click away.
            <AddPrinterForm
              key={step.serialNumber}
              prefill={step}
              onAdded={() => setStep(undefined)}
            />
          )}
        </>
      )}
    </Page>
  );
}

interface AddPrinterFormProps {
  prefill?: DiscoveredPrinterResponse;
  onAdded: () => void;
}

function AddPrinterForm({ prefill, onAdded }: AddPrinterFormProps) {
  const navigate = useNavigate();
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
      name: prefill?.name ?? '',
      brand: prefill?.brand ?? '',
      modelCode: prefill?.modelCode ?? '',
      ipAddress: prefill?.ipAddress ?? '',
      serialNumber: prefill?.serialNumber ?? '',
      accessCode: '',
    },
  });

  // A discovered printer arrives with everything but the access code filled in.
  useEffect(() => {
    if (prefill) form.setFocus('accessCode');
  }, [prefill, form]);

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

  // Discovery already knows brand/model, unless the printer reported a model the catalog can't map.
  const showModelPickers =
    !prefill?.modelCode ||
    !catalog?.brands?.some(
      (b) =>
        b.brand === prefill.brand &&
        b.models?.includes(prefill.modelCode ?? ''),
    );

  const verifyMutation =
    useOpenFarmApiEndpointsPrintersVerifyPrinterConnectionEndpoint();

  const registerMutation =
    useOpenFarmApiEndpointsPrintersRegisterPrinterEndpoint({
      mutation: {
        onSuccess: () => {
          toast.success('Printer added.');
          queryClient.invalidateQueries({
            queryKey:
              getOpenFarmApiEndpointsPrintersListFleetEndpointQueryKey(),
          });
          queryClient.invalidateQueries({
            queryKey:
              getOpenFarmApiEndpointsPrintersGetDiscoveredPrintersEndpointQueryKey(),
          });
          markClean();
          onAdded();
        },
        onError: () => {
          toast.error('Could not add printer.');
        },
      },
    });

  function handleBrandChange(newBrand: string) {
    // Radix's hidden form control can emit an empty value while options mount.
    if (!newBrand) return;
    form.setValue('brand', newBrand);
    const models = catalog?.brands?.find((b) => b.brand === newBrand)?.models;
    form.setValue('modelCode', models?.[0] ?? '');
  }

  // FR-001a: verify-before-add — Add runs the connection check first and only registers once it
  // passes, so there's no separate "verified" state for the operator to chase.
  async function onSubmit(values: AddFormValues) {
    const { brand, modelCode, ipAddress, serialNumber, accessCode } = values;
    try {
      await verifyMutation.mutateAsync({
        data: { brand, modelCode, ipAddress, serialNumber, accessCode },
      });
    } catch {
      return;
    }
    registerMutation.mutate({ data: values });
  }

  const catalogUnavailable = catalogQuery.isError;
  const adding = verifyMutation.isPending || registerMutation.isPending;

  return (
    <>
      <Form {...form}>
        <form
          onSubmit={form.handleSubmit(onSubmit)}
          className="flex flex-1 flex-col gap-6"
        >
          <FieldGroup className="flex-1 gap-6">
            <Card>
              <CardHeader>
                <CardTitle>
                  {prefill
                    ? `${prefill.brand} ${prefill.modelCode ?? prefill.rawModel}`
                    : 'Connection'}
                </CardTitle>
                {prefill && (
                  <CardDescription>
                    {prefill.ipAddress} · {prefill.serialNumber}
                  </CardDescription>
                )}
              </CardHeader>
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
                {showModelPickers && (
                  <>
                    <FormField
                      control={form.control}
                      name="brand"
                      render={({ field }) => (
                        <FormItem
                          data-disabled={
                            catalogQuery.isLoading || catalogUnavailable
                          }
                        >
                          <FormLabel>Brand</FormLabel>
                          <Select
                            name={field.name}
                            value={field.value}
                            onValueChange={handleBrandChange}
                            disabled={
                              catalogQuery.isLoading || catalogUnavailable
                            }
                          >
                            <FormControl>
                              <SelectTrigger
                                ref={field.ref}
                                onBlur={field.onBlur}
                                className="w-full"
                              >
                                <SelectValue />
                              </SelectTrigger>
                            </FormControl>
                            <SelectContent>
                              <SelectGroup>
                                {(catalog?.brands ?? []).map((b) => (
                                  <SelectItem
                                    key={b.brand}
                                    value={b.brand ?? ''}
                                  >
                                    {b.brand}
                                  </SelectItem>
                                ))}
                              </SelectGroup>
                            </SelectContent>
                          </Select>
                          <FormMessage />
                        </FormItem>
                      )}
                    />
                    <FormField
                      control={form.control}
                      name="modelCode"
                      render={({ field }) => (
                        <FormItem
                          data-disabled={
                            catalogQuery.isLoading || catalogUnavailable
                          }
                        >
                          <FormLabel>Model</FormLabel>
                          <Select
                            name={field.name}
                            value={field.value}
                            onValueChange={(next) => {
                              if (next) field.onChange(next);
                            }}
                            disabled={
                              catalogQuery.isLoading || catalogUnavailable
                            }
                          >
                            <FormControl>
                              <SelectTrigger
                                ref={field.ref}
                                onBlur={field.onBlur}
                                className="w-full"
                              >
                                <SelectValue placeholder="Model" />
                              </SelectTrigger>
                            </FormControl>
                            <SelectContent>
                              <SelectGroup>
                                {modelsForBrand.map((model) => (
                                  <SelectItem key={model} value={model}>
                                    {model}
                                  </SelectItem>
                                ))}
                              </SelectGroup>
                            </SelectContent>
                          </Select>
                          <FormMessage />
                        </FormItem>
                      )}
                    />
                  </>
                )}
                {!prefill && (
                  <>
                    <FormField
                      control={form.control}
                      name="ipAddress"
                      render={({ field }) => (
                        <FormItem>
                          <FormLabel>IP address</FormLabel>
                          <FormControl>
                            <Input {...field} />
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
                            <Input {...field} />
                          </FormControl>
                          <FormMessage />
                        </FormItem>
                      )}
                    />
                  </>
                )}
                <FormField
                  control={form.control}
                  name="accessCode"
                  render={({ field }) => (
                    <FormItem>
                      <FormLabel>Access code</FormLabel>
                      <FormControl>
                        <Input type="password" {...field} />
                      </FormControl>
                      <FormDescription>
                        Printer screen → Settings → Network.
                      </FormDescription>
                      <FormMessage />
                    </FormItem>
                  )}
                />
                {catalogUnavailable && (
                  <p className="text-sm text-destructive sm:col-span-2">
                    Could not load printer catalog.
                  </p>
                )}
                {verifyMutation.isError && !adding && (
                  <p className="text-sm text-destructive sm:col-span-2">
                    {prefill
                      ? 'Connection failed. Check access code.'
                      : 'Connection failed. Check IP, serial, access code.'}
                  </p>
                )}
                <p className="text-sm text-muted-foreground sm:col-span-2">
                  LAN mode disconnects Bambu Cloud and Bambu Handy.
                </p>
              </CardContent>
            </Card>

            <FormFooter
              end={
                <>
                  <Button
                    type="button"
                    variant="outline"
                    onClick={() => navigate('/printers')}
                  >
                    Cancel
                  </Button>
                  <Button type="submit" disabled={adding || catalogUnavailable}>
                    {adding && <Spinner data-icon="inline-start" />}
                    {adding ? 'Adding…' : 'Add printer'}
                  </Button>
                </>
              }
            />
          </FieldGroup>
        </form>
      </Form>

      <ConfirmLeaveDialog />
    </>
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
  const navigate = useNavigate();
  const detailQuery = useOpenFarmApiEndpointsPrintersGetPrinterDetailEndpoint(
    id ?? '',
  );
  const printer = detailQuery.data?.data as PrinterDetailResponse | undefined;

  if (detailQuery.isLoading || !printer) {
    return (
      <Page>
        <PageHeader
          above={
            <BackLink to="/printers" className="mb-1">
              Back to fleet
            </BackLink>
          }
          title="Edit printer"
        />
        {detailQuery.isLoading ? (
          <Skeleton className="h-64 w-full" />
        ) : (
          <EmptyState
            title="Printer not found"
            description="Removed or invalid link."
            action={
              <Button variant="outline" onClick={() => navigate('/printers')}>
                Back to fleet
              </Button>
            }
          />
        )}
      </Page>
    );
  }

  return (
    <EditPrinterFormLoaded
      printerId={printer.id ?? id ?? ''}
      printerName={printer.name ?? ''}
      brand={printer.brand ?? ''}
      modelCode={printer.modelCode ?? ''}
      serialNumber={printer.serialNumber ?? ''}
      nozzleDiameterMm={printer.telemetry?.nozzleDiameterMm}
      nozzleConfirmed={printer.telemetry?.nozzleConfirmed ?? false}
      currentIpAddress={printer.ipAddress ?? ''}
      machineProfileId={printer.machineProfileId}
      machineProfileName={printer.machineProfileName}
      machineProfileIssue={printer.machineProfileIssue}
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
  nozzleDiameterMm?: number | null;
  nozzleConfirmed: boolean;
  machineProfileId: string | null | undefined;
  machineProfileName: string | null | undefined;
  machineProfileIssue?: string | null;
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
  nozzleDiameterMm,
  nozzleConfirmed,
  machineProfileId,
  machineProfileName,
  machineProfileIssue,
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

  // The profile and build-plate pickers live outside react-hook-form, so they join the dirty check here.
  const { ConfirmLeaveDialog, markClean } = useUnsavedChangesPrompt(
    form.formState.isDirty ||
      selectedMachineProfileId !== (machineProfileId ?? null) ||
      selectedProcessProfileId !== (processProfileId ?? null) ||
      selectedBuildPlateType !== (buildPlateType ?? null),
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
        toast.success('Printer saved.');
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
        toast.error('Could not save printer.');
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
    <Page className="flex-1">
      <PageHeader
        above={
          <BackLink to="/printers" className="mb-1">
            Back to fleet
          </BackLink>
        }
        title={`Edit ${printerName}`}
        description={`${brand} ${modelCode} · ${serialNumber}`}
      />

      <Form {...form}>
        <form
          onSubmit={form.handleSubmit(onSubmit)}
          className="flex flex-1 flex-col gap-6"
        >
          <FieldGroup className="flex-1 gap-6">
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
                      <FormLabel>Access code</FormLabel>
                      <FormControl>
                        <Input
                          type="password"
                          placeholder="Blank = keep current"
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
                      Connection failed. Check IP, access code.
                    </p>
                  )}
                {connectionChanged && verified && !verifyMutation.isPending && (
                  <p className="text-sm text-status-complete">
                    Connection verified.
                  </p>
                )}
                <div className="flex flex-col gap-1.5">
                  <span className="text-sm font-medium">Machine profile</span>
                  <p className="text-xs text-muted-foreground">
                    {nozzleConfirmed
                      ? `Nozzle: ${nozzleDiameterMm} mm (reported)`
                      : 'Waiting for nozzle data…'}
                  </p>
                  {selectedMachineProfileId === machineProfileId &&
                    machineProfileIssue && (
                      <p className="text-sm text-destructive">
                        {machineProfileIssue}
                      </p>
                    )}
                  <ProfilePickerCombobox
                    kind="machine"
                    printerId={printerId}
                    disabled={!nozzleConfirmed}
                    disabledPlaceholder="Waiting for nozzle data…"
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
                    disabled={!selectedMachineProfileId || !nozzleConfirmed}
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
                      <SelectGroup>
                        <SelectItem value={NO_BUILD_PLATE_VALUE}>
                          Not selected
                        </SelectItem>
                        {(compatibleBuildPlateTypes ?? []).map((option) => (
                          <SelectItem
                            key={option.value}
                            value={option.value ?? ''}
                          >
                            {option.label}
                          </SelectItem>
                        ))}
                      </SelectGroup>
                    </SelectContent>
                  </Select>
                </div>
              </CardContent>
            </Card>

            <FormFooter
              end={
                <>
                  <Button
                    type="button"
                    variant="outline"
                    onClick={() => navigate('/printers')}
                  >
                    Cancel
                  </Button>
                  <Button type="submit" disabled={saveDisabled}>
                    {editMutation.isPending && (
                      <Spinner data-icon="inline-start" />
                    )}
                    {editMutation.isPending ? 'Saving…' : 'Save'}
                  </Button>
                </>
              }
            />
          </FieldGroup>
        </form>
      </Form>

      <ConfirmLeaveDialog />
    </Page>
  );
}
