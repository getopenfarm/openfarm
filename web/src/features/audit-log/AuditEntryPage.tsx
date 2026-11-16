import {
  ChevronDownIcon,
  ChevronRightIcon,
  ExternalLinkIcon,
  FilterIcon,
  type LucideIcon,
} from 'lucide-react';
import { useState } from 'react';
import { Link, useLocation, useParams } from 'react-router';
import { BackLink } from '@/components/back-link/BackLink';
import { ErrorState } from '@/components/empty-state/EmptyState';
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card';
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
  useOpenFarmApiEndpointsActivityLogGetAuditEntryEndpoint,
  useOpenFarmApiEndpointsActivityLogGetRelatedAuditEntriesEndpoint,
} from '@/lib/api/generated/audit-log/audit-log';
import type {
  AuditEntryResponse,
  AuditFieldChange,
} from '@/lib/api/generated/endpoints.schemas';
import { formatDateTime } from '@/lib/formatDateTime';
import { cn } from '@/lib/utils';
import {
  ACTION_LABELS,
  auditActor,
  auditActorLine,
  auditFieldLabel,
  auditFieldValue,
  auditSetChanges,
  auditSummary,
  auditValue,
  CHANNEL_LABELS,
  humanize,
} from './auditFormat';

function AuditRelatedLink({
  to,
  title,
  description,
  icon: Icon,
  back,
}: {
  to: string;
  title: string;
  description: string;
  icon?: LucideIcon;
  back?: string;
}) {
  return (
    <Link
      to={to}
      state={back ? { back } : undefined}
      className="flex min-w-0 items-center gap-3 rounded-md border p-3 hover:bg-muted/50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
    >
      {Icon && <Icon className="size-4 shrink-0 text-muted-foreground" />}
      <span className="flex min-w-0 flex-1 flex-col gap-1">
        <span className="truncate text-sm font-medium">{title}</span>
        <span className="truncate text-xs text-muted-foreground">
          {description}
        </span>
      </span>
      <ChevronRightIcon className="size-4 shrink-0 text-muted-foreground" />
    </Link>
  );
}

function AuditChangeRow({
  change,
  action,
  recordType,
}: {
  change: AuditFieldChange;
  action?: string | null;
  recordType?: string;
}) {
  const set = action === 'update' ? auditSetChanges(change) : null;
  return (
    <TableRow>
      <TableCell
        className="w-1/3 align-top text-muted-foreground"
        title={change.field}
      >
        {auditFieldLabel(change.field)}
      </TableCell>
      {set ? (
        <TableCell colSpan={2} className="whitespace-normal">
          <div className="flex flex-wrap gap-2">
            {set.added.map((value) => (
              <Badge key={`added-${value}`} kind="green">
                + {value}
              </Badge>
            ))}
            {set.removed.map((value) => (
              <Badge key={`removed-${value}`} kind="red">
                − {value}
              </Badge>
            ))}
            {set.added.length === 0 && set.removed.length === 0 && (
              <span>
                {auditFieldValue(recordType, change.field, change.after)}
              </span>
            )}
          </div>
        </TableCell>
      ) : (
        <>
          {action !== 'create' && (
            <TableCell
              className={cn(
                'max-w-md whitespace-pre-wrap break-words align-top',
                action === 'update' && 'text-muted-foreground line-through',
              )}
            >
              {auditFieldValue(recordType, change.field, change.before)}
            </TableCell>
          )}
          {action !== 'delete' && (
            <TableCell className="max-w-md whitespace-pre-wrap break-words align-top">
              {auditFieldValue(recordType, change.field, change.after)}
            </TableCell>
          )}
        </>
      )}
    </TableRow>
  );
}

function AuditEntryContent({
  entry,
  siblings,
  relatedLoading,
  relatedError,
  retryRelated,
  back,
}: {
  entry: AuditEntryResponse;
  siblings: { items?: AuditEntryResponse[]; totalCount?: number } | undefined;
  relatedLoading: boolean;
  relatedError: boolean;
  retryRelated: () => void;
  back: string;
}) {
  const [expanded, setExpanded] = useState(false);
  const typeLabel = humanize(entry.entityType);
  const title = entry.entityLabel ?? `${typeLabel} #${entry.entityId}`;
  const credential = entry.channel === 'Token' ? 'token' : 'app';
  const history = new URLSearchParams({
    entityType: entry.entityType ?? '',
    entityId: entry.entityId ?? '',
  });
  const relatedEntries = siblings?.items ?? [];
  const shown = expanded ? relatedEntries : relatedEntries.slice(0, 3);
  return (
    <Page>
      <PageHeader
        above={
          <div className="mb-2 flex flex-col gap-2">
            <BackLink to={back}>Audit log</BackLink>
            <p className="text-xs text-muted-foreground">
              {typeLabel} ·{' '}
              {ACTION_LABELS[entry.action ?? ''] ?? humanize(entry.eventType)}
            </p>
          </div>
        }
        title={title}
        description={`${auditActorLine(entry)} · ${entry.timestampUtc ? formatDateTime(entry.timestampUtc) : '—'}`}
      />
      <div className="grid min-w-0 items-start gap-6 lg:grid-cols-[minmax(0,1fr)_20rem]">
        <Card className="min-w-0 gap-4">
          <CardHeader>
            <CardTitle>
              {entry.action === 'create'
                ? 'Created with'
                : entry.action === 'delete'
                  ? 'What was removed'
                  : 'What changed'}
            </CardTitle>
            {entry.historical && (
              <CardDescription>
                Historical entry. Field details and names may not have been
                recorded.
              </CardDescription>
            )}
          </CardHeader>
          <CardContent>
            {entry.changes?.length ? (
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Field</TableHead>
                    {entry.action !== 'create' && (
                      <TableHead>
                        {entry.action === 'delete' ? 'Value' : 'Before'}
                      </TableHead>
                    )}
                    {entry.action !== 'delete' && (
                      <TableHead>
                        {entry.action === 'create' ? 'Value' : 'After'}
                      </TableHead>
                    )}
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {entry.changes.map((change) => (
                    <AuditChangeRow
                      key={`${change.field}-${auditValue(change.before)}-${auditValue(change.after)}`}
                      change={change}
                      action={entry.action}
                      recordType={entry.entityType}
                    />
                  ))}
                </TableBody>
              </Table>
            ) : (
              <p className="text-sm text-muted-foreground">
                No field details were recorded for this change.
              </p>
            )}
          </CardContent>
        </Card>
        <div className="flex min-w-0 flex-col gap-4">
          <Card className="gap-4">
            <CardHeader>
              <CardTitle>How it arrived</CardTitle>
            </CardHeader>
            <CardContent className="flex flex-col gap-4">
              <dl className="grid grid-cols-[auto_minmax(0,1fr)] gap-x-4 gap-y-3 text-sm">
                <dt className="text-muted-foreground">Who</dt>
                <dd className="break-words">{auditActor(entry)}</dd>
                {entry.details?.initiatedByUserId && (
                  <>
                    <dt className="text-muted-foreground">Initiated by</dt>
                    <dd className="break-words">
                      {entry.details.initiatedByName ??
                        entry.details.initiatedByUserId}
                    </dd>
                  </>
                )}
                <dt className="text-muted-foreground">Channel</dt>
                <dd>{CHANNEL_LABELS[entry.channel ?? ''] ?? 'Unknown'}</dd>
                {(entry.channel === 'Token' || entry.channel === 'App') && (
                  <>
                    <dt className="text-muted-foreground">
                      {entry.channel === 'Token' ? 'Token' : 'App'}
                    </dt>
                    <dd className="break-words">
                      {entry.credentialLabel ?? '—'}
                    </dd>
                  </>
                )}
                {entry.operation && (
                  <>
                    <dt className="text-muted-foreground">Request</dt>
                    <dd className="break-all font-mono text-xs">
                      {entry.operation}
                    </dd>
                  </>
                )}
                <dt className="text-muted-foreground">When</dt>
                <dd className="break-words">
                  <time
                    dateTime={entry.timestampUtc}
                    title={entry.timestampUtc}
                  >
                    {entry.timestampUtc
                      ? formatDateTime(entry.timestampUtc)
                      : '—'}
                  </time>
                  <span className="mt-1 block text-xs text-muted-foreground">
                    {Intl.DateTimeFormat().resolvedOptions().timeZone}
                  </span>
                </dd>
              </dl>
              {entry.credentialId &&
                (entry.channel === 'Token' || entry.channel === 'App') && (
                  <AuditRelatedLink
                    to={`/audit-log?credentialId=${entry.credentialId}`}
                    title={`Everything this ${credential} changed`}
                    description={
                      entry.credentialLabel ?? CHANNEL_LABELS[entry.channel]
                    }
                    icon={FilterIcon}
                  />
                )}
            </CardContent>
          </Card>
          <Card className="gap-4">
            <CardHeader>
              <CardTitle>The {typeLabel.toLowerCase()}</CardTitle>
            </CardHeader>
            <CardContent className="flex flex-col gap-3">
              {entry.href ? (
                <AuditRelatedLink
                  to={entry.href}
                  title={title}
                  description="Open this record"
                  icon={ExternalLinkIcon}
                />
              ) : (
                <p className="text-sm text-muted-foreground">
                  {entry.action === 'delete'
                    ? 'It was removed.'
                    : 'It has no page of its own, or no longer exists.'}
                </p>
              )}
              <AuditRelatedLink
                to={`/audit-log?${history}`}
                title="Its whole history"
                description={`Every change to this ${typeLabel.toLowerCase()}`}
                icon={FilterIcon}
              />
            </CardContent>
          </Card>
          {(relatedLoading || relatedError || !!siblings?.totalCount) && (
            <Card className="gap-4">
              <CardHeader>
                <CardTitle>Changed in the same request</CardTitle>
              </CardHeader>
              <CardContent className="flex flex-col gap-3">
                {relatedError ? (
                  <ErrorState
                    description="Could not load related changes."
                    onRetry={retryRelated}
                    bordered={false}
                  />
                ) : relatedLoading ? (
                  <Skeleton className="h-12 w-full" />
                ) : (
                  <>
                    <ul className="flex flex-col gap-2">
                      {shown.map((other) => (
                        <li key={other.id}>
                          <AuditRelatedLink
                            to={`/audit-log/${other.id}`}
                            title={`${humanize(other.entityType)}: ${other.entityLabel ?? `#${other.entityId}`}`}
                            description={auditSummary(other, 1)}
                            back={back}
                          />
                        </li>
                      ))}
                    </ul>
                    {relatedEntries.length > 3 && (
                      <Button
                        variant="ghost"
                        size="sm"
                        aria-expanded={expanded}
                        onClick={() => setExpanded(!expanded)}
                      >
                        <ChevronDownIcon data-icon="inline-start" />
                        {expanded
                          ? 'Show less'
                          : `Show ${relatedEntries.length - 3} more`}
                      </Button>
                    )}
                    {entry.correlationId &&
                      (expanded || relatedEntries.length <= 3) &&
                      (siblings?.totalCount ?? 0) > relatedEntries.length && (
                        <Link
                          className="text-sm text-primary underline-offset-4 hover:underline"
                          to={`/audit-log?correlationId=${encodeURIComponent(entry.correlationId)}`}
                        >
                          See all changes in this request
                        </Link>
                      )}
                  </>
                )}
              </CardContent>
            </Card>
          )}
        </div>
      </div>
    </Page>
  );
}

export function AuditEntryPage() {
  const { id = '' } = useParams();
  const location = useLocation();
  const query = useOpenFarmApiEndpointsActivityLogGetAuditEntryEndpoint(id);
  const related =
    useOpenFarmApiEndpointsActivityLogGetRelatedAuditEntriesEndpoint(id);
  const entry = query.data?.status === 200 ? query.data.data : undefined;
  const siblings = related.data?.status === 200 ? related.data.data : undefined;
  const back =
    typeof location.state?.back === 'string' &&
    location.state.back.startsWith('/audit-log')
      ? location.state.back
      : '/audit-log';
  if (query.isLoading)
    return (
      <Page>
        <BackLink to={back}>Audit log</BackLink>
        <Skeleton className="h-48 w-full" />
      </Page>
    );
  if (!entry)
    return (
      <Page>
        <BackLink to={back}>Audit log</BackLink>
        <ErrorState
          description="Could not load audit entry."
          onRetry={() => query.refetch()}
        />
      </Page>
    );
  return (
    <AuditEntryContent
      key={entry.id}
      entry={entry}
      siblings={siblings}
      relatedLoading={related.isLoading}
      relatedError={related.isError}
      retryRelated={() => {
        void related.refetch();
      }}
      back={back}
    />
  );
}
