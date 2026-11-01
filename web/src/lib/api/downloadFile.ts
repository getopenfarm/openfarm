import { toast } from 'sonner';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import { authenticatedFetch } from '@/lib/auth/session';

/** Saves an API response as a file named by its Content-Disposition. On failure, toasts the API's detail, else `failure`. */
export async function downloadFile(
  path: string,
  fallbackName: string,
  failure: string,
) {
  const response = await authenticatedFetch(path).catch(() => undefined);
  if (!response?.ok) {
    toast.error(
      response ? await extractErrorDetail(response, failure) : failure,
    );
    return;
  }
  const url = URL.createObjectURL(await response.blob());
  const link = document.createElement('a');
  link.href = url;
  link.download =
    response.headers
      .get('content-disposition')
      ?.match(/filename="?([^";]+)/)?.[1] ?? fallbackName;
  link.click();
  URL.revokeObjectURL(url);
}
