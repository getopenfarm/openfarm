import { zodResolver } from '@hookform/resolvers/zod';
import type { ColumnDef } from '@tanstack/react-table';
import { KeyIcon } from 'lucide-react';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { toast } from 'sonner';
import { z } from 'zod';
import { DataTable } from '@/components/data-table/DataTable';
import { DataTableColumnHeader } from '@/components/data-table/DataTableColumnHeader';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { PageHeader } from '@/components/page-header/PageHeader';
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
import { Skeleton } from '@/components/ui/skeleton';
import type { ConnectionSecretResponse } from '@/lib/api/generated/endpoints.schemas';
import {
  useOpenFarmApiEndpointsConnectionSecretsCreateConnectionSecretEndpoint,
  useOpenFarmApiEndpointsConnectionSecretsListConnectionSecretsEndpoint,
} from '@/lib/api/generated/connection-secrets/connection-secrets';

const createSchema = z.object({
  printerSerialNumber: z.string().min(1, 'Printer serial number is required'),
  plainAccessCode: z.string().min(1, 'Access code is required'),
});

type CreateFormValues = z.infer<typeof createSchema>;

const columns: ColumnDef<ConnectionSecretResponse>[] = [
  {
    id: 'printerSerialNumber',
    accessorKey: 'printerSerialNumber',
    header: ({ column }) => (
      <DataTableColumnHeader column={column} title="Printer serial number" />
    ),
  },
  {
    id: 'maskedAccessCode',
    accessorKey: 'maskedAccessCode',
    header: ({ column }) => (
      <DataTableColumnHeader column={column} title="Access code" />
    ),
  },
  {
    id: 'createdAtUtc',
    header: ({ column }) => (
      <DataTableColumnHeader column={column} title="Created" />
    ),
    cell: ({ row }) =>
      row.original.createdAtUtc
        ? new Date(row.original.createdAtUtc).toLocaleString()
        : '',
  },
];

export function ConnectionSecretsPage() {
  const [open, setOpen] = useState(false);
  const listQuery =
    useOpenFarmApiEndpointsConnectionSecretsListConnectionSecretsEndpoint();

  const form = useForm<CreateFormValues>({
    resolver: zodResolver(createSchema),
    defaultValues: { printerSerialNumber: '', plainAccessCode: '' },
  });

  const createMutation =
    useOpenFarmApiEndpointsConnectionSecretsCreateConnectionSecretEndpoint({
      mutation: {
        onSuccess: () => {
          toast.success('Connection secret created.');
          form.reset();
          setOpen(false);
          listQuery.refetch();
        },
        onError: () => {
          toast.error('Failed to create connection secret.');
        },
      },
    });

  function onSubmit(values: CreateFormValues) {
    createMutation.mutate({ data: values });
  }

  const secrets = listQuery.data?.data ?? [];

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title="Connection secrets"
        description="Encrypted LAN access codes used to connect directly to printers."
        action={
          <Dialog open={open} onOpenChange={setOpen}>
            <DialogTrigger asChild>
              <Button>Add credential</Button>
            </DialogTrigger>
            <DialogContent>
              <DialogHeader>
                <DialogTitle>Add connection credential</DialogTitle>
                <DialogDescription>
                  The access code is encrypted at rest and never shown again
                  after creation.
                </DialogDescription>
              </DialogHeader>
              <Form {...form}>
                <form
                  onSubmit={form.handleSubmit(onSubmit)}
                  className="flex flex-col gap-4"
                >
                  <FormField
                    control={form.control}
                    name="printerSerialNumber"
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel>Printer serial number</FormLabel>
                        <FormControl>
                          <Input {...field} />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                  <FormField
                    control={form.control}
                    name="plainAccessCode"
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel>Access code</FormLabel>
                        <FormControl>
                          <Input type="password" {...field} />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                  <DialogFooter>
                    <Button type="submit" disabled={createMutation.isPending}>
                      {createMutation.isPending ? 'Saving…' : 'Save'}
                    </Button>
                  </DialogFooter>
                </form>
              </Form>
            </DialogContent>
          </Dialog>
        }
      />

      {listQuery.isLoading ? (
        <Skeleton className="h-64 w-full" />
      ) : listQuery.isError ? (
        <ErrorState
          description="Couldn't load connection secrets."
          onRetry={() => listQuery.refetch()}
        />
      ) : secrets.length === 0 ? (
        <EmptyState
          icon={<KeyIcon className="size-8 text-muted-foreground" />}
          title="No credentials yet"
          description="Use Add credential above to register a printer's LAN access code."
        />
      ) : (
        <DataTable columns={columns} data={secrets} />
      )}
    </div>
  );
}
