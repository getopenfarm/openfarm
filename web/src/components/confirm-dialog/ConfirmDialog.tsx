import type { ReactNode, RefObject } from 'react';
import { Alert, AlertDescription } from '@/components/ui/alert';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog';
import { buttonVariants } from '@/components/ui/button';
import { Spinner } from '@/components/ui/spinner';

interface ConfirmDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;
  description: string;
  confirmLabel: string;
  onConfirm: () => void;
  isPending?: boolean;
  confirmDisabled?: boolean;
  hideConfirm?: boolean;
  children?: ReactNode;
  secondaryActions?: ReactNode;
  cancelLabel?: string;
  closeOnConfirm?: boolean;
  error?: string;
  destructive?: boolean;
  returnFocusRef?: RefObject<HTMLElement | null>;
  fallbackFocusRef?: RefObject<HTMLElement | null>;
}

export function ConfirmDialog({
  open,
  onOpenChange,
  title,
  description,
  confirmLabel,
  onConfirm,
  isPending = false,
  confirmDisabled = false,
  hideConfirm = false,
  children,
  secondaryActions,
  cancelLabel = 'Cancel',
  closeOnConfirm = true,
  error,
  destructive = false,
  returnFocusRef,
  fallbackFocusRef,
}: ConfirmDialogProps) {
  return (
    <AlertDialog
      open={open}
      onOpenChange={(next) => !isPending && onOpenChange(next)}
    >
      <AlertDialogContent
        className="max-h-[calc(100dvh-2rem)] overflow-y-auto"
        onCloseAutoFocus={(event) => {
          const preferred = returnFocusRef?.current;
          const target =
            preferred && !preferred.matches(':disabled')
              ? preferred
              : fallbackFocusRef?.current;
          if (target) {
            event.preventDefault();
            target.focus();
          }
        }}
      >
        <AlertDialogHeader>
          <AlertDialogTitle>{title}</AlertDialogTitle>
          <AlertDialogDescription className="break-words">
            {description}
          </AlertDialogDescription>
        </AlertDialogHeader>
        {children}
        {error && (
          <Alert variant="destructive">
            <AlertDescription className="whitespace-pre-line break-words">
              {error}
            </AlertDescription>
          </Alert>
        )}
        <AlertDialogFooter>
          <AlertDialogCancel disabled={isPending}>
            {cancelLabel}
          </AlertDialogCancel>
          {secondaryActions}
          {!hideConfirm && (
            <AlertDialogAction
              disabled={isPending || confirmDisabled}
              className={buttonVariants({
                variant: destructive ? 'destructive' : 'default',
              })}
              onClick={(event) => {
                if (!closeOnConfirm) event.preventDefault();
                if (!isPending && !confirmDisabled) onConfirm();
              }}
            >
              {isPending && (
                <Spinner data-icon="inline-start" aria-hidden="true" />
              )}
              {confirmLabel}
            </AlertDialogAction>
          )}
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
