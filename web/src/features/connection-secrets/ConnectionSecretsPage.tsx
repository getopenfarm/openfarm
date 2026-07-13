import { zodResolver } from '@hookform/resolvers/zod';
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
import { Skeleton } from '@/components/ui/skeleton';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import {
  useOpenFarmApiEndpointsConnectionSecretsCreateConnectionSecretEndpoint,
  useOpenFarmApiEndpointsConnectionSecretsListConnectionSecretsEndpoint,
} from '@/lib/api/generated/connection-secrets/connection-secrets';

const createSchema = z.object({
  printerSerialNumber: z.string().min(1, 'Printer serial number is required'),
  plainAccessCode: z.string().min(1, 'Access code is required'),
});

type CreateFormValues = z.infer<typeof createSchema>;

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
    <div className="flex flex-col gap-4">
      <div className="flex items-center justify-end">
        <Dialog open={open} onOpenChange={setOpen}>
          <DialogTrigger asChild>
            <Button>Add credential</Button>
          </DialogTrigger>
          <DialogContent>
            <DialogHeader>
              <DialogTitle>Add connection credential</DialogTitle>
              <DialogDescription>
                The access code is encrypted at rest and never shown again after
                creation.
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
      </div>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Printer serial number</TableHead>
            <TableHead>Access code</TableHead>
            <TableHead>Created</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {listQuery.isLoading ? (
            Array.from({ length: 3 }).map((_, index) => (
              // biome-ignore lint/suspicious/noArrayIndexKey: static skeleton placeholder rows
              <TableRow key={`skeleton-${index}`}>
                <TableCell>
                  <Skeleton className="h-4 w-32" />
                </TableCell>
                <TableCell>
                  <Skeleton className="h-4 w-12" />
                </TableCell>
                <TableCell>
                  <Skeleton className="h-4 w-24" />
                </TableCell>
              </TableRow>
            ))
          ) : listQuery.isError ? (
            <TableRow>
              <TableCell
                colSpan={3}
                className="py-8 text-center text-muted-foreground text-sm"
              >
                Couldn't load connection secrets.{' '}
                <Button
                  variant="link"
                  className="h-auto p-0"
                  onClick={() => listQuery.refetch()}
                >
                  Retry
                </Button>
              </TableCell>
            </TableRow>
          ) : (
            secrets.map((secret) => (
              <TableRow key={secret.id}>
                <TableCell>{secret.printerSerialNumber}</TableCell>
                <TableCell>{secret.maskedAccessCode}</TableCell>
                <TableCell>
                  {secret.createdAtUtc
                    ? new Date(secret.createdAtUtc).toLocaleString()
                    : ''}
                </TableCell>
              </TableRow>
            ))
          )}
        </TableBody>
      </Table>
    </div>
  );
}
