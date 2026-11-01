import type { Control, FieldValues, Path } from 'react-hook-form';
import { z } from 'zod';
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card';
import { FieldGroup } from '@/components/ui/field';
import {
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from '@/components/ui/form';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';

export const runDetailsSchema = z.object({
  name: z.string().max(200, 'At most 200 characters.'),
  dueDate: z.string(),
  notes: z.string(),
});

export type RunDetailsValues = z.infer<typeof runDetailsSchema>;

/** Blank fields are sent as null: an unnamed run is labelled from its lines. */
export const runDetailsRequest = (values: RunDetailsValues) => ({
  name: values.name.trim() || null,
  dueDate: values.dueDate || null,
  notes: values.notes.trim() || null,
});

/** Name, due date and notes: display only — the queue order alone decides what prints next. */
export function RunDetailsFields<T extends FieldValues & RunDetailsValues>({
  control,
  optional = false,
  disabled = false,
}: {
  control: Control<T>;
  optional?: boolean;
  disabled?: boolean;
}) {
  return (
    <Card>
      <CardHeader>
        <CardTitle>{optional ? 'Run details (optional)' : 'Details'}</CardTitle>
        {optional && (
          <CardDescription>
            Give the run a name and due date to help track the order.
          </CardDescription>
        )}
      </CardHeader>
      <CardContent>
        <FieldGroup className="grid gap-4 sm:grid-cols-[minmax(0,1fr)_12rem]">
          <FormField
            control={control}
            name={'name' as Path<T>}
            render={({ field }) => (
              <FormItem data-disabled={disabled || undefined}>
                <FormLabel>Name</FormLabel>
                <FormControl>
                  <Input
                    disabled={disabled}
                    placeholder="Named from its items when left blank"
                    {...field}
                  />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />
          <FormField
            control={control}
            name={'dueDate' as Path<T>}
            render={({ field }) => (
              <FormItem data-disabled={disabled || undefined}>
                <FormLabel>Due</FormLabel>
                <FormControl>
                  <Input type="date" disabled={disabled} {...field} />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />
          <FormField
            control={control}
            name={'notes' as Path<T>}
            render={({ field }) => (
              <FormItem
                className="sm:col-span-2"
                data-disabled={disabled || undefined}
              >
                <FormLabel>Notes</FormLabel>
                <FormControl>
                  <Textarea rows={2} disabled={disabled} {...field} />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />
        </FieldGroup>
      </CardContent>
    </Card>
  );
}
