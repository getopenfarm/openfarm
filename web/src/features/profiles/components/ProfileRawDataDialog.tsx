import { zodResolver } from '@hookform/resolvers/zod';
import { useEffect } from 'react';
import { useForm } from 'react-hook-form';
import { z } from 'zod';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogScrollableContent,
  DialogTitle,
} from '@/components/ui/dialog';
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormMessage,
} from '@/components/ui/form';
import { Textarea } from '@/components/ui/textarea';

const rawDataFormSchema = z.object({
  rawDataJson: z.string().min(1, 'Raw data is required'),
});

type RawDataFormValues = z.infer<typeof rawDataFormSchema>;

interface ProfileRawDataDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  name?: string;
  rawDataJson?: string;
  isLoading?: boolean;
  onSave?: (rawDataJson: string) => Promise<void>;
  isSaving?: boolean;
}

function formatRawDataJson(rawDataJson: string): string {
  try {
    return JSON.stringify(JSON.parse(rawDataJson), null, 2);
  } catch {
    // Should never happen (data-model.md guarantees well-formed JSON), but shown as-is rather
    // than crashing the dialog if it somehow does.
    return rawDataJson;
  }
}

// FR-011/FR-012/FR-013 — view-only until a row's data has loaded, then editable; Save submits the
// raw textarea contents as-is (server re-validates JSON well-formedness) and a 422 response is
// shown inline without disturbing the last-saved value (US4 Acceptance Scenario 2).
export function ProfileRawDataDialog({
  open,
  onOpenChange,
  name,
  rawDataJson,
  isLoading,
  onSave,
  isSaving,
}: ProfileRawDataDialogProps) {
  const form = useForm<RawDataFormValues>({
    resolver: zodResolver(rawDataFormSchema),
    defaultValues: { rawDataJson: '' },
  });

  useEffect(() => {
    if (open && rawDataJson !== undefined) {
      form.reset({ rawDataJson: formatRawDataJson(rawDataJson) });
    }
  }, [open, rawDataJson, form]);

  async function onSubmit(values: RawDataFormValues) {
    if (!onSave) {
      return;
    }

    form.clearErrors('root');
    try {
      await onSave(values.rawDataJson);
    } catch (error) {
      form.setError('root', {
        message:
          error instanceof Error
            ? error.message
            : 'Could not save this profile — check that the raw data is valid JSON.',
      });
    }
  }

  const hasContent = !isLoading && rawDataJson !== undefined;

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogScrollableContent
        className="max-w-2xl sm:max-w-2xl"
        header={<DialogTitle>{name ?? 'Profile'}</DialogTitle>}
        footer={
          hasContent && onSave ? (
            <Button
              type="submit"
              form="profile-raw-data-form"
              disabled={isSaving}
            >
              {isSaving ? 'Saving…' : 'Save'}
            </Button>
          ) : undefined
        }
      >
        {isLoading && <p className="text-sm text-muted-foreground">Loading…</p>}
        {hasContent && (
          <Form {...form}>
            <form
              id="profile-raw-data-form"
              onSubmit={form.handleSubmit(onSubmit)}
              className="flex flex-col gap-3"
            >
              <FormField
                control={form.control}
                name="rawDataJson"
                render={({ field }) => (
                  <FormItem>
                    <FormControl>
                      <Textarea
                        {...field}
                        disabled={!onSave}
                        className="h-96 font-mono text-xs"
                        spellCheck={false}
                      />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
              {form.formState.errors.root && (
                <p className="text-sm text-destructive" role="alert">
                  {form.formState.errors.root.message}
                </p>
              )}
            </form>
          </Form>
        )}
      </DialogScrollableContent>
    </Dialog>
  );
}
