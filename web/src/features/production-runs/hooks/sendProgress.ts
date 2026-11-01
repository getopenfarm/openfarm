export type PrintSendStage =
  | 'Slicing'
  | 'Uploading'
  | 'Starting'
  | 'Retrying'
  | 'Sent'
  | 'Ended';

/** Where sending a job to its printer is. Pushed live while it happens; never persisted. */
export interface PrintJobSendProgress {
  printJobId: string;
  stage: PrintSendStage;
  percent: number | null;
  attempt: number;
  error: string | null;
}

export const sendProgressKey = (jobId: string) => [
  'print-job-send-progress',
  jobId,
];
