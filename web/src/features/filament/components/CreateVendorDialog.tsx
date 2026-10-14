import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { toast } from 'sonner';
import { z } from 'zod';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from '@/components/ui/dialog';
import { FieldGroup } from '@/components/ui/field';
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from '@/components/ui/form';
import { Input } from '@/components/ui/input';
import { Spinner } from '@/components/ui/spinner';
import type { VendorResponse } from '@/lib/api/generated/endpoints.schemas';
import {
  getOpenFarmApiEndpointsVendorsListVendorsEndpointQueryKey,
  useOpenFarmApiEndpointsVendorsCreateVendorEndpoint,
} from '@/lib/api/generated/vendors/vendors';

const schema = z.object({
  name: z.string().min(1, 'Name is required'),
});

type FormValues = z.infer<typeof schema>;

interface CreateVendorDialogProps {
  onCreated?: (vendor: VendorResponse) => void;
}

export function CreateVendorDialog({ onCreated }: CreateVendorDialogProps) {
  const [open, setOpen] = useState(false);
  const queryClient = useQueryClient();

  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { name: '' },
  });

  const createMutation = useOpenFarmApiEndpointsVendorsCreateVendorEndpoint({
    mutation: {
      onSuccess: (response) => {
        toast.success('Vendor added.');
        form.reset();
        setOpen(false);
        queryClient.invalidateQueries({
          queryKey: getOpenFarmApiEndpointsVendorsListVendorsEndpointQueryKey(),
        });
        onCreated?.(response.data as VendorResponse);
      },
      onError: () => {
        toast.error('Could not add vendor.');
      },
    },
  });

  function onSubmit(values: FormValues) {
    createMutation.mutate({ data: values });
  }

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        <Button variant="outline">Add vendor</Button>
      </DialogTrigger>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Add vendor</DialogTitle>
          <DialogDescription>
            Filament manufacturers and brands you stock materials from.
          </DialogDescription>
        </DialogHeader>
        <Form {...form}>
          <form
            onSubmit={(event) => {
              // Also stops the submit from bubbling to an ancestor <form> in the React tree —
              // this dialog is a self-contained quick action that may be rendered inside a
              // page's own form (e.g. FilamentFormPage), and React's portal event bubbling
              // follows the component tree, not the DOM tree, so an un-stopped submit here
              // would also trigger the outer form's onSubmit.
              event.stopPropagation();
              form.handleSubmit(onSubmit)(event);
            }}
            className="flex flex-col gap-4"
          >
            <FieldGroup className="gap-4">
              <FormField
                control={form.control}
                name="name"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>Name</FormLabel>
                    <FormControl>
                      <Input placeholder="e.g. Bambu Lab" {...field} />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
              <DialogFooter>
                <DialogClose asChild>
                  <Button type="button" variant="outline">
                    Cancel
                  </Button>
                </DialogClose>
                <Button type="submit" disabled={createMutation.isPending}>
                  {createMutation.isPending && (
                    <Spinner data-icon="inline-start" />
                  )}
                  {createMutation.isPending ? 'Saving…' : 'Save'}
                </Button>
              </DialogFooter>
            </FieldGroup>
          </form>
        </Form>
      </DialogContent>
    </Dialog>
  );
}
