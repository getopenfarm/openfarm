import { CheckIcon, PlusIcon, WrenchIcon } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useNavigate } from 'react-router';
import { DataTableSearch } from '@/components/data-table/DataTableSearch';
import {
  DataTableTabCount,
  DataTableTabsList,
  DataTableToolbar,
} from '@/components/data-table/DataTableToolbar';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { KpiStrip } from '@/components/kpi-card/KpiStrip';
import { PermissionButton as Button } from '@/components/PermissionButton';
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Skeleton } from '@/components/ui/skeleton';
import { Tabs, TabsContent, TabsTrigger } from '@/components/ui/tabs';
import type { MaintenanceTasksResponse } from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsMaintenanceListMaintenanceTasksEndpoint } from '@/lib/api/generated/maintenance/maintenance';
import { EnrollmentsTable } from './components/EnrollmentsTable';
import { MaintenanceHistory } from './components/MaintenanceHistory';
import { TasksTable } from './components/TasksTable';
import { needsAttention, useMaintenanceEnrollments } from './maintenance';

type Tab = 'attention' | 'all' | 'tasks' | 'history';
const TRIGGERS = [
  { value: 'all', label: 'Any trigger' },
  { value: 'Hours', label: 'Hours' },
  { value: 'Days', label: 'Days' },
  { value: 'Prints', label: 'Prints' },
];

export function MaintenancePage() {
  const navigate = useNavigate();
  const [tab, setTab] = useState<Tab>('attention');
  const [search, setSearch] = useState('');
  const [trigger, setTrigger] = useState('all');
  const { query, items } = useMaintenanceEnrollments();
  const tasksQuery =
    useOpenFarmApiEndpointsMaintenanceListMaintenanceTasksEndpoint();
  const tasks =
    (tasksQuery.data?.data as MaintenanceTasksResponse | undefined)?.items ??
    [];

  const attention = useMemo(() => items.filter(needsAttention), [items]);
  const shown = useMemo(() => {
    const needle = search.trim().toLowerCase();
    return (tab === 'attention' ? attention : items).filter(
      (e) =>
        (trigger === 'all' || e.drivingTrigger === trigger) &&
        (!needle ||
          `${e.printerName} ${e.taskName}`.toLowerCase().includes(needle)),
    );
  }, [tab, attention, items, search, trigger]);
  const count = (state: string) =>
    items.filter((e) => e.state === state).length;
  const enrolledPrinters = new Set(items.map((e) => e.printerId)).size;
  const filtered = search.trim() !== '' || trigger !== 'all';
  const listTab = tab === 'attention' || tab === 'all';

  const addTask = (
    <Button
      permission="maintenance:manage"
      onClick={() => navigate('/maintenance/new')}
    >
      <PlusIcon data-icon="inline-start" />
      Add task
    </Button>
  );

  function enrollmentsContent() {
    if (query.isLoading) return <Skeleton className="h-64 w-full" />;
    if (query.isError && !query.data)
      return (
        <ErrorState
          title="Could not load maintenance"
          onRetry={() => query.refetch()}
        />
      );
    if (items.length === 0)
      return (
        <EmptyState
          icon={<WrenchIcon className="size-8 text-muted-foreground" />}
          title="No printers enrolled"
          description="Add a task, or Add recommended on a printer."
          action={addTask}
        />
      );
    if (shown.length === 0)
      return filtered ? (
        <EmptyState
          title="No matches"
          description="Change the search or filter."
        />
      ) : (
        <EmptyState
          icon={<CheckIcon className="size-8 text-muted-foreground" />}
          title="Nothing due"
        />
      );
    return <EnrollmentsTable enrollments={shown} />;
  }

  return (
    <Page>
      <PageHeader title="Maintenance" action={addTask} />
      <KpiStrip
        cells={[
          {
            label: 'Overdue',
            value: query.data ? count('Overdue') : '—',
            dotClassName: 'bg-kind-red',
          },
          {
            label: 'Due soon',
            value: query.data ? count('DueSoon') : '—',
            dotClassName: 'bg-kind-amber',
          },
          {
            label: 'OK',
            value: query.data ? count('Ok') : '—',
            dotClassName: 'bg-kind-green',
          },
          {
            label: 'Printers enrolled',
            value: query.data ? enrolledPrinters : '—',
          },
        ]}
      />
      <Tabs
        value={tab}
        onValueChange={(next) => setTab(next as Tab)}
        className="min-w-0 gap-4"
      >
        <DataTableToolbar
          search={
            listTab ? (
              <DataTableSearch
                value={search}
                onValueChange={setSearch}
                label="Search by printer or task"
                placeholder="Search printers or tasks…"
              />
            ) : undefined
          }
          actions={
            listTab ? (
              <Select value={trigger} onValueChange={setTrigger}>
                <SelectTrigger
                  aria-label="Filter by trigger"
                  className="w-full sm:w-36"
                >
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectGroup>
                    {TRIGGERS.map((t) => (
                      <SelectItem key={t.value} value={t.value}>
                        {t.label}
                      </SelectItem>
                    ))}
                  </SelectGroup>
                </SelectContent>
              </Select>
            ) : undefined
          }
        >
          <DataTableTabsList aria-label="Maintenance views">
            <TabsTrigger value="attention">
              Needs attention
              <DataTableTabCount
                count={query.data ? attention.length : undefined}
              />
            </TabsTrigger>
            <TabsTrigger value="all">
              All
              <DataTableTabCount
                count={query.data ? items.length : undefined}
              />
            </TabsTrigger>
            <TabsTrigger value="tasks">
              Tasks
              <DataTableTabCount
                count={tasksQuery.data ? tasks.length : undefined}
              />
            </TabsTrigger>
            <TabsTrigger value="history">History</TabsTrigger>
          </DataTableTabsList>
        </DataTableToolbar>
        <TabsContent value="attention" className="flex flex-col gap-4">
          {enrollmentsContent()}
        </TabsContent>
        <TabsContent value="all" className="flex flex-col gap-4">
          {enrollmentsContent()}
        </TabsContent>
        <TabsContent value="tasks" className="flex flex-col gap-4">
          {tasksQuery.isLoading ? (
            <Skeleton className="h-64 w-full" />
          ) : tasksQuery.isError && !tasksQuery.data ? (
            <ErrorState
              title="Could not load tasks"
              onRetry={() => tasksQuery.refetch()}
            />
          ) : tasks.length === 0 ? (
            <EmptyState
              icon={<WrenchIcon className="size-8 text-muted-foreground" />}
              title="No maintenance tasks"
              action={addTask}
            />
          ) : (
            <TasksTable tasks={tasks} />
          )}
        </TabsContent>
        <TabsContent value="history" className="flex flex-col gap-4">
          <MaintenanceHistory />
        </TabsContent>
      </Tabs>
    </Page>
  );
}
