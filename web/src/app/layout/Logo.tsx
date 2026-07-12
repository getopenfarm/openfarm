export function Logo() {
  return (
    <div className="flex items-center gap-2 px-2">
      <img src="/favicon.svg" alt="" className="size-6 shrink-0" />
      <span className="truncate font-heading text-sm font-semibold group-data-[collapsible=icon]:hidden">
        OpenFarm
      </span>
    </div>
  );
}
