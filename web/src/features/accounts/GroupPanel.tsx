import { useQueryClient } from '@tanstack/react-query';
import {
  LockIcon,
  MoreHorizontalIcon,
  PlusIcon,
  ShieldCheckIcon,
  Trash2Icon,
} from 'lucide-react';
import { useState } from 'react';
import { toast } from 'sonner';
import { ConfirmDialog } from '@/components/confirm-dialog/ConfirmDialog';
import { Button } from '@/components/ui/button';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import {
  Sheet,
  SheetContent,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { Spinner } from '@/components/ui/spinner';
import { Switch } from '@/components/ui/switch';
import { Textarea } from '@/components/ui/textarea';
import { useIsMobile } from '@/hooks/use-mobile';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import { httpClient } from '@/lib/api/httpClient';
import { cn } from '@/lib/utils';
import { AccessEditor } from './AccessEditor';
import { adminKey, type GroupResponse, useAdministration } from './api';
import { listOf } from './describe';
import { adminPill, sectionLabel } from './styles';

const members = (n = 0) => `${n} member${n === 1 ? '' : 's'}`;

/** Group list with the selected group's editor beside it (a sheet on mobile). */
export function GroupPanel({
  groups,
  selectedId,
  onSelect,
}: {
  groups: GroupResponse[];
  selectedId?: string;
  onSelect: (id?: string) => void;
}) {
  const isMobile = useIsMobile();
  const client = useQueryClient();
  const [sheetOpen, setSheetOpen] = useState(false);
  const [creating, setCreating] = useState(false);
  const selected = groups.find((g) => g.id === selectedId);

  async function createGroup() {
    setCreating(true);
    try {
      const result = await httpClient<{ data: { id: string } }>(
        `${adminKey}/groups`,
        {
          method: 'POST',
          body: JSON.stringify({
            name: 'New group',
            description: '',
            grants: [],
          }),
        },
      );
      await client.invalidateQueries({ queryKey: [adminKey] });
      onSelect(result.data.id);
      if (isMobile) setSheetOpen(true);
    } catch (e) {
      toast.error(await extractErrorDetail(e, 'Could not create the group'));
    } finally {
      setCreating(false);
    }
  }
  const onDeleted = () => {
    setSheetOpen(false);
    onSelect(groups.find((g) => g.id !== selectedId)?.id);
  };

  return (
    <div className="md:grid md:h-[calc(100dvh-140px)] md:grid-cols-[220px_minmax(0,1fr)]">
      <nav
        aria-label="Groups"
        className="flex flex-col gap-0.5 py-1 md:overflow-y-auto md:border-r md:py-3 md:pr-4"
      >
        <button
          type="button"
          disabled={creating}
          onClick={() => void createGroup()}
          className="mb-1.5 flex w-full items-center gap-1.5 rounded-md border border-dashed px-3 py-2 text-[12.5px] text-muted-foreground hover:border-primary hover:text-primary"
        >
          <PlusIcon className="size-[15px]" />
          New group
        </button>
        {groups.map((g) => (
          <button
            type="button"
            key={g.id}
            aria-current={g.id === selectedId || undefined}
            onClick={() => {
              onSelect(g.id);
              if (isMobile) setSheetOpen(true);
            }}
            className={cn(
              'flex w-full flex-col gap-0.5 rounded-md px-3 py-[9px] text-left hover:bg-muted',
              g.id === selectedId && 'bg-accent hover:bg-accent',
            )}
          >
            <span className="flex items-center justify-between gap-1.5">
              <span className="text-[13px] font-medium">{g.name}</span>
              {g.isProtected && (
                <ShieldCheckIcon className="size-3.5 text-primary" />
              )}
            </span>
            <span className="text-[11.5px] text-muted-foreground">
              {members(g.memberCount)} ·{' '}
              {g.isProtected
                ? 'Full access'
                : `${g.grants?.length ?? 0} grants`}
            </span>
          </button>
        ))}
      </nav>
      {!isMobile && selected && (
        <GroupDetail key={selected.id} group={selected} onDeleted={onDeleted} />
      )}
      {isMobile && (
        <Sheet open={sheetOpen && !!selected} onOpenChange={setSheetOpen}>
          <SheetContent
            side="bottom"
            aria-describedby={undefined}
            className="h-dvh gap-0 overflow-hidden rounded-none p-0"
          >
            {selected && (
              <GroupDetail
                key={selected.id}
                group={selected}
                onDeleted={onDeleted}
                mobile
              />
            )}
          </SheetContent>
        </Sheet>
      )}
    </div>
  );
}

type Draft = { name: string; description: string; grants: string[] };
const draftOf = (g: GroupResponse): Draft => ({
  name: g.name ?? '',
  description: g.description ?? '',
  grants: g.grants ?? [],
});

function GroupDetail({
  group,
  onDeleted,
  mobile = false,
}: {
  group: GroupResponse;
  onDeleted: () => void;
  mobile?: boolean;
}) {
  const { data } = useAdministration();
  const client = useQueryClient();
  const [draft, setDraft] = useState(() => draftOf(group));
  const [saving, setSaving] = useState(false);
  const [deleteOpen, setDeleteOpen] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const locked = !!group.isProtected;
  const isDirty = JSON.stringify(draft) !== JSON.stringify(draftOf(group));
  const areas = data?.areas ?? [];
  const permissions = data?.permissions ?? [];
  const adminOnly = new Set(
    areas.filter((a) => a.administratorOnly).map((a) => a.key),
  );
  const grantable = permissions.filter((p) => !adminOnly.has(p.area)).length;
  const areaLabel = new Map(areas.map((a) => [a.key, a.label]));
  const explicit = permissions
    .filter((p) => p.isExplicit)
    .map((p) => `${areaLabel.get(p.area)} › ${p.label}`);

  async function save() {
    setSaving(true);
    try {
      await httpClient(`${adminKey}/groups/${group.id}`, {
        method: 'PUT',
        body: JSON.stringify(draft),
      });
      await client.invalidateQueries({ queryKey: [adminKey] });
      toast.success('Group saved');
    } catch (e) {
      toast.error(await extractErrorDetail(e, 'Could not save the group'));
    } finally {
      setSaving(false);
    }
  }
  async function remove() {
    setDeleting(true);
    try {
      await httpClient(`${adminKey}/groups/${group.id}`, { method: 'DELETE' });
      setDeleteOpen(false);
      toast.success('Group deleted');
      onDeleted();
      await client.invalidateQueries({ queryKey: [adminKey] });
    } catch (e) {
      toast.error(await extractErrorDetail(e, 'Could not delete the group'));
    } finally {
      setDeleting(false);
    }
  }

  const menu = (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button
          variant="outline"
          size="icon-sm"
          aria-label={`Actions for ${group.name}`}
        >
          <MoreHorizontalIcon />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        <DropdownMenuItem
          variant="destructive"
          disabled={locked || !!group.memberCount}
          onSelect={() => setDeleteOpen(true)}
        >
          <Trash2Icon />
          Delete group
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
  const badges = (
    <div className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
      {locked && (
        <span className={adminPill}>
          <ShieldCheckIcon className="size-[13px]" />
          Administrator
        </span>
      )}
      {locked && (
        <span className="inline-flex items-center gap-1">
          <LockIcon className="size-3" />
          Protected
        </span>
      )}
      <span>{members(group.memberCount)}</span>
    </div>
  );
  const nameInput = (
    <input
      aria-label="Group name"
      value={draft.name}
      disabled={locked}
      onChange={(e) => setDraft({ ...draft, name: e.target.value })}
      className="w-full border-b border-transparent bg-transparent p-0 text-lg font-semibold outline-none focus:border-primary disabled:cursor-not-allowed disabled:opacity-70"
    />
  );
  const body = (
    <div className="flex flex-col gap-3.5">
      {mobile && nameInput}
      <Textarea
        rows={2}
        aria-label="Description"
        placeholder="Describe what this group is for…"
        value={draft.description}
        disabled={locked}
        onChange={(e) => setDraft({ ...draft, description: e.target.value })}
        className="min-h-[60px]"
      />
      <div className="flex items-center justify-between gap-4 rounded-md border px-3.5 py-3">
        <div className="min-w-0">
          <div className="text-[13.5px] font-medium">Administrator</div>
          <p className="mt-0.5 text-xs leading-[1.45] text-muted-foreground">
            Bypasses the permission grid entirely — full access to every area,
            plus user and group management.
          </p>
        </div>
        <Switch aria-label="Administrator group" checked={locked} disabled />
      </div>
      <div className="mt-1 flex items-center justify-between">
        <div className={sectionLabel}>Members can</div>
        {!locked && (
          <span className="text-xs text-muted-foreground">
            {draft.grants.length} of {grantable} granted
          </span>
        )}
      </div>
      {locked ? (
        <div className="flex items-start gap-3.5 rounded-md border border-primary/25 bg-primary/10 p-4">
          <ShieldCheckIcon className="mt-0.5 size-5 shrink-0 text-primary" />
          <div>
            <div className="mb-1 text-[13px] font-semibold">
              Every permission
            </div>
            <p className="text-[12.5px] leading-normal text-muted-foreground">
              Administrators hold every permission, including managing users and
              groups, which no other group can be given.
            </p>
          </div>
        </div>
      ) : (
        <>
          <AccessEditor
            areas={areas}
            permissions={permissions}
            value={draft.grants}
            disabled={saving}
            onChange={(grants) => setDraft({ ...draft, grants })}
          />
          {explicit.length > 0 && (
            <p className="flex items-center gap-1.5 text-xs text-muted-foreground">
              <LockIcon className="size-3 shrink-0" />
              No level includes {listOf(explicit, 'or')}. Each has its own
              switch, so it is only ever given on purpose.
            </p>
          )}
        </>
      )}
    </div>
  );
  const actions = isDirty && (
    <>
      <Button
        variant="outline"
        size="sm"
        onClick={() => setDraft(draftOf(group))}
      >
        Reset
      </Button>
      <Button size="sm" disabled={saving} onClick={() => void save()}>
        {saving && <Spinner data-icon="inline-start" />}
        Save changes
      </Button>
    </>
  );
  const confirm = (
    <ConfirmDialog
      open={deleteOpen}
      onOpenChange={setDeleteOpen}
      title={`Delete group "${group.name}"?`}
      description="This cannot be undone."
      confirmLabel="Delete"
      destructive
      isPending={deleting}
      closeOnConfirm={false}
      onConfirm={() => void remove()}
    />
  );

  if (mobile)
    return (
      <>
        <SheetHeader className="flex-row items-start justify-between gap-2 border-b px-[22px] pt-[22px] pb-4">
          <div className="min-w-0">
            <div className="font-mono text-[11.5px] text-muted-foreground">
              Edit group
            </div>
            <SheetTitle className="text-[19px] font-semibold">
              {group.name}
            </SheetTitle>
          </div>
          <div className="mr-8">{menu}</div>
        </SheetHeader>
        <div className="flex min-h-0 flex-1 flex-col gap-4 overflow-y-auto px-[22px] py-5">
          {badges}
          {body}
        </div>
        {actions && (
          <div className="flex shrink-0 justify-end gap-2 border-t px-5 py-3.5">
            {actions}
          </div>
        )}
        {confirm}
      </>
    );
  return (
    <section
      aria-label={group.name}
      className="flex min-w-0 flex-col gap-3.5 overflow-y-auto px-6 py-5"
    >
      <div className="flex items-start justify-between gap-3">
        <div className="flex min-w-0 flex-1 flex-col gap-1.5">
          {nameInput}
          {badges}
        </div>
        {menu}
      </div>
      {body}
      {actions && (
        <div className="mt-1 flex items-center justify-end gap-2 border-t pt-3">
          {actions}
        </div>
      )}
      {confirm}
    </section>
  );
}
