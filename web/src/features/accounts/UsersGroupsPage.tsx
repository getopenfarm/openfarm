import type { ColumnDef } from '@tanstack/react-table';
import {
  MonitorIcon,
  PencilIcon,
  PlusIcon,
  ShieldCheckIcon,
  ShieldIcon,
  Trash2Icon,
  TriangleAlertIcon,
} from 'lucide-react';
import { useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router';
import { toast } from 'sonner';
import { ConfirmDialog } from '@/components/confirm-dialog/ConfirmDialog';
import { DataTable } from '@/components/data-table/DataTable';
import { DataTableRowActions } from '@/components/data-table/DataTableRowActions';
import {
  DataTableTabCount,
  DataTableTabsList,
  DataTableToolbar,
} from '@/components/data-table/DataTableToolbar';
import { ErrorState } from '@/components/empty-state/EmptyState';
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Avatar, AvatarFallback } from '@/components/ui/avatar';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
  DropdownMenuGroup,
  DropdownMenuItem,
} from '@/components/ui/dropdown-menu';
import { Skeleton } from '@/components/ui/skeleton';
import { Tabs, TabsContent, TabsTrigger } from '@/components/ui/tabs';
import { useIsMobile } from '@/hooks/use-mobile';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import {
  type AccountResponse,
  type GroupResponse,
  useAdministration,
  useRemoveUser,
} from './api';
import { GroupPanel } from './GroupPanel';

const initials = (u: AccountResponse) =>
  (u.name || u.email || '')
    .split(/[\s@._-]/)
    .map((p) => p[0])
    .join('')
    .toUpperCase()
    .slice(0, 2);

function UserCell({ u }: { u: AccountResponse }) {
  return (
    <div className="flex items-center gap-2.5">
      <Avatar className="size-[30px]">
        <AvatarFallback className="bg-primary/10 text-[11px] font-bold text-primary">
          {initials(u)}
        </AvatarFallback>
      </Avatar>
      <div className="min-w-0">
        <Link
          to={`/users-groups/users/${u.id}`}
          className="block font-medium hover:underline"
        >
          {u.name || 'Unnamed user'}
        </Link>
        <span className="block truncate text-xs text-muted-foreground">
          {u.email}
        </span>
      </div>
    </div>
  );
}
function GroupChips({
  u,
  groups,
}: {
  u: AccountResponse;
  groups: GroupResponse[];
}) {
  if (!u.groups?.length)
    return (
      <span className="inline-flex items-center gap-1 text-xs text-amber">
        <TriangleAlertIcon className="size-3" />
        No groups
      </span>
    );
  return (
    <div className="flex flex-wrap gap-1">
      {u.groups.map((g) =>
        groups.find((x) => x.id === g.id)?.isProtected ? (
          <Badge key={g.id} kind="violet" className="text-[11.5px]">
            {g.name}
          </Badge>
        ) : (
          <Badge
            key={g.id}
            variant="outline"
            className="bg-accent text-[11.5px]"
          >
            {g.name}
          </Badge>
        ),
      )}
    </div>
  );
}
function Access({ u }: { u: AccountResponse }) {
  const muted = 'inline-flex items-center gap-1 text-xs text-muted-foreground';
  if (!u.isEnabled) return <span className={muted}>Disabled</span>;
  if (u.isAdministrator)
    return (
      <Badge kind="blue">
        <ShieldCheckIcon />
        Administrator
      </Badge>
    );
  if (!u.groups?.length)
    return (
      <span className="text-[12.5px] text-muted-foreground">No access</span>
    );
  return (
    <span className={muted}>
      <ShieldIcon className="size-3" />
      Standard
    </span>
  );
}
function Sessions({ u }: { u: AccountResponse }) {
  return u.sessionCount ? (
    <span className="inline-flex items-center gap-1.5 font-mono text-[11.5px] opacity-70">
      <MonitorIcon className="size-[13px]" />
      {u.sessionCount} active
    </span>
  ) : (
    <span className="text-[12.5px] text-muted-foreground">None</span>
  );
}

export function UsersGroupsPage() {
  const query = useAdministration();
  const isMobile = useIsMobile();
  const removeUser = useRemoveUser();
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();
  const tab = params.get('tab') === 'groups' ? 'groups' : 'users';
  const setTab = (v: string) =>
    setParams(v === 'groups' ? { tab: 'groups' } : {}, { replace: true });
  const [selectedGroupId, setSelectedGroupId] = useState<string>();
  const openUser = (u: AccountResponse) => navigate(`users/${u.id}`);
  const [removing, setRemoving] = useState<AccountResponse | null>(null);
  // Apart from removing, so the title holds while the dialog closes.
  const [removeOpen, setRemoveOpen] = useState(false);
  const askRemove = (u: AccountResponse) => {
    setRemoving(u);
    setRemoveOpen(true);
  };
  const users = query.data?.users ?? [];
  const groups = query.data?.groups ?? [];
  const groupId = groups.some((g) => g.id === selectedGroupId)
    ? selectedGroupId
    : groups[0]?.id;

  const columns: ColumnDef<AccountResponse>[] = [
    {
      id: 'user',
      header: 'User',
      cell: ({ row }) => <UserCell u={row.original} />,
    },
    {
      id: 'groups',
      header: 'Groups',
      cell: ({ row }) => <GroupChips u={row.original} groups={groups} />,
    },
    {
      id: 'access',
      header: 'Access',
      cell: ({ row }) => <Access u={row.original} />,
    },
    {
      id: 'sessions',
      header: 'Sessions',
      cell: ({ row }) => <Sessions u={row.original} />,
    },
    {
      id: 'created',
      header: 'Created',
      cell: ({ row }) => (
        <span className="text-[12.5px] tabular-nums">
          {row.original.createdAtUtc?.slice(0, 10)}
        </span>
      ),
    },
    {
      id: 'actions',
      cell: ({ row }) => (
        <DataTableRowActions
          label={row.original.name || row.original.email || 'user'}
        >
          <DropdownMenuGroup>
            <DropdownMenuItem onSelect={() => openUser(row.original)}>
              <PencilIcon />
              Edit user
            </DropdownMenuItem>
            <DropdownMenuItem
              variant="destructive"
              onSelect={() => askRemove(row.original)}
            >
              <Trash2Icon />
              Remove user
            </DropdownMenuItem>
          </DropdownMenuGroup>
        </DataTableRowActions>
      ),
    },
  ];
  const noGroup = users.filter((u) => !u.groups?.length).length;

  return (
    <Page>
      <PageHeader
        title="Users & groups"
        description="Access control for your farm · Administrator area"
        action={
          tab === 'users' ? (
            <Button onClick={() => navigate('users/new')}>
              <PlusIcon data-icon="inline-start" />
              Add user
            </Button>
          ) : undefined
        }
      />
      <Tabs value={tab} onValueChange={setTab} className="gap-4">
        <DataTableToolbar>
          <DataTableTabsList aria-label="Users and groups">
            <TabsTrigger value="users">
              Users{' '}
              <DataTableTabCount
                count={query.data ? users.length : undefined}
              />
            </TabsTrigger>
            <TabsTrigger value="groups">
              Groups{' '}
              <DataTableTabCount
                count={query.data ? groups.length : undefined}
              />
            </TabsTrigger>
          </DataTableTabsList>
          {tab === 'groups' && (
            <span className="ml-auto hidden text-xs text-muted-foreground md:inline">
              Administrators hold every permission · a user has everything any
              of their groups grants
            </span>
          )}
        </DataTableToolbar>
        <TabsContent value={tab} className="flex flex-col gap-4">
          {query.isError && query.data && (
            <ErrorState
              title="Could not refresh users and groups"
              description="Showing previous results."
              onRetry={() => query.refetch()}
            />
          )}
          {query.isError && !query.data ? (
            <ErrorState
              title="Could not load users and groups"
              onRetry={() => void query.refetch()}
            />
          ) : !query.data ? (
            <Skeleton className="h-64 w-full" />
          ) : tab === 'users' ? (
            <div className="flex flex-col gap-3">
              <div className="flex flex-wrap items-center justify-between gap-3 px-0.5 py-2.5 text-[12.5px] whitespace-nowrap text-muted-foreground max-md:text-xs">
                <div>
                  <b className="font-semibold text-foreground">
                    {users.length}
                  </b>{' '}
                  user
                  {users.length === 1 ? '' : 's'} ·{' '}
                  <b className="font-semibold text-foreground">{noGroup}</b>{' '}
                  without a group
                </div>
              </div>
              <DataTable
                data={users}
                columns={columns}
                getRowId={(u) => u.id ?? ''}
                onRowClick={openUser}
                className={
                  isMobile
                    ? 'rounded-none border-0 [&_ul]:gap-2.5 [&_ul]:p-0 sm:[&_ul]:grid-cols-1'
                    : undefined
                }
                renderCard={
                  isMobile
                    ? ({ original: u }) => (
                        <div className="flex flex-col gap-2.5 rounded-lg border bg-card p-3.5 shadow-sm">
                          <UserCell u={u} />
                          <GroupChips u={u} groups={groups} />
                          <div className="flex items-center justify-between gap-3">
                            <div className="flex items-center gap-3">
                              <Access u={u} />
                              <Sessions u={u} />
                            </div>
                            <span className="text-xs text-muted-foreground tabular-nums">
                              {u.createdAtUtc?.slice(0, 10)}
                            </span>
                          </div>
                        </div>
                      )
                    : undefined
                }
              />
            </div>
          ) : (
            <GroupPanel
              groups={groups}
              selectedId={groupId}
              onSelect={setSelectedGroupId}
            />
          )}
        </TabsContent>
      </Tabs>
      <ConfirmDialog
        open={removeOpen}
        onOpenChange={setRemoveOpen}
        title={`Remove ${removing?.name || removing?.email}?`}
        description="This cannot be undone."
        confirmLabel="Remove"
        destructive
        isPending={removeUser.isPending}
        closeOnConfirm={false}
        onConfirm={() => {
          if (!removing?.id) return;
          removeUser
            .mutateAsync(removing.id)
            .then(() => {
              toast.success('User removed.');
              setRemoveOpen(false);
            })
            .catch(async (e) =>
              toast.error(
                await extractErrorDetail(e, 'Could not remove user.'),
              ),
            );
        }}
      />
    </Page>
  );
}
