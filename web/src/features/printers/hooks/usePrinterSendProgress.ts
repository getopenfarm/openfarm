import { skipToken } from '@tanstack/react-query';
import {
  type PrintJobSendProgress,
  printerSendProgressKey,
} from '@/features/production-runs/hooks/sendProgress';
import { useAuthorizedQuery as useQuery } from '@/lib/auth/useAuthorizedQuery';

/** The send in progress on `printerId`, filled by the farm hub; undefined when none. */
export function usePrinterSendProgress(printerId: string) {
  return useQuery<PrintJobSendProgress>({
    queryKey: printerSendProgressKey(printerId),
    queryFn: skipToken,
  }).data;
}
