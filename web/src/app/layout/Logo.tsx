export function Logo() {
  return (
    <div className="flex items-center gap-2.5 px-2.5 pt-1.5 pb-3 group-data-[collapsible=icon]:justify-center group-data-[collapsible=icon]:px-0">
      <img src="/favicon.svg" alt="" className="size-8 shrink-0" />
      <span className="truncate font-heading text-[15px] font-semibold tracking-tight group-data-[collapsible=icon]:hidden">
        OpenFarm
      </span>
    </div>
  );
}
