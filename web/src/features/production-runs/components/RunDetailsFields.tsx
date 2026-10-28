import type { Control, FieldValues, Path } from 'react-hook-form';
import { z } from 'zod';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
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
}: {
  control: Control<T>;
}) {
  return (
    <Card>
      <CardHeader>
        <CardTitle>Details</CardTitle>
      </CardHeader>
      <CardContent className="grid gap-4 sm:grid-cols-[minmax(0,1fr)_12rem]">
        <FormField
          control={control}
          name={'name' as Path<T>}
          render={({ field }) => (
            <FormItem>
              <FormLabel>Name</FormLabel>
              <FormControl>
                <Input placeholder="Blank = named after its parts" {...field} />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />
        <FormField
          control={control}
          name={'dueDate' as Path<T>}
          render={({ field }) => (
            <FormItem>
              <FormLabel>Due</FormLabel>
              <FormControl>
                <Input type="date" {...field} />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />
        <FormField
          control={control}
          name={'notes' as Path<T>}
          render={({ field }) => (
            <FormItem className="sm:col-span-2">
              <FormLabel>Notes</FormLabel>
              <FormControl>
                <Textarea rows={2} {...field} />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />
      </CardContent>
    </Card>
  );
}
