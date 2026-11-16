import { toast } from 'sonner';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import { authenticatedFetch } from '@/lib/auth/session';

/** Saves an API response as a file named by its Content-Disposition. On failure, toasts the API's detail, else `failure`. */
export async function downloadFile(
  path: string,
  fallbackName: string,
  failure: string,
) {
  let url: string | undefined;
  try {
    const response = await authenticatedFetch(path);
    if (!response.ok) {
      toast.error(await extractErrorDetail(response, failure));
      return;
    }
    url = URL.createObjectURL(await response.blob());
    const link = document.createElement('a');
    link.href = url;
    link.download =
      response.headers
        .get('content-disposition')
        ?.match(/filename="?([^";]+)/)?.[1] ?? fallbackName;
    link.click();
  } catch {
    toast.error(failure);
  } finally {
    if (url) URL.revokeObjectURL(url);
  }
}
