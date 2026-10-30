import {
  createContext,
  type Dispatch,
  type ReactNode,
  type SetStateAction,
  useContext,
  useLayoutEffect,
  useMemo,
  useState,
} from 'react';
import { createPortal } from 'react-dom';
import { useLocation } from 'react-router';

interface BreadcrumbOutletValue {
  target: HTMLDivElement | null;
  setTarget: (target: HTMLDivElement | null) => void;
  routeKey: string;
  owner: { routeKey: string; token: symbol } | null;
  setOwner: Dispatch<
    SetStateAction<{ routeKey: string; token: symbol } | null>
  >;
}

const BreadcrumbContext = createContext<BreadcrumbOutletValue | null>(null);

/** Route-scoped so an old page can never leave its trail on the next route. */
export function BreadcrumbOutletProvider({
  children,
}: {
  children: ReactNode;
}) {
  const location = useLocation();
  const [target, setTarget] = useState<HTMLDivElement | null>(null);
  const [owner, setOwner] = useState<{
    routeKey: string;
    token: symbol;
  } | null>(null);
  const value = useMemo(
    () => ({ target, setTarget, owner, setOwner, routeKey: location.key }),
    [target, owner, location.key],
  );
  return (
    <BreadcrumbContext.Provider value={value}>
      {children}
    </BreadcrumbContext.Provider>
  );
}

export function BreadcrumbOutlet({ fallback }: { fallback: ReactNode }) {
  const context = useContext(BreadcrumbContext);
  return (
    <div className="min-w-0 flex-1">
      {context && context.owner?.routeKey === context.routeKey
        ? null
        : fallback}
      <div ref={context?.setTarget} />
    </div>
  );
}

/** Portals retain page context, including the library's drag/drop provider. */
export function PageBreadcrumb({ children }: { children: ReactNode }) {
  const context = useContext(BreadcrumbContext);
  const setOwner = context?.setOwner;
  const routeKey = context?.routeKey;
  const [owner] = useState(() => Symbol('page breadcrumb'));
  useLayoutEffect(() => {
    if (!routeKey) return;
    setOwner?.({ routeKey, token: owner });
    return () =>
      setOwner?.((current) => (current?.token === owner ? null : current));
  }, [setOwner, owner, routeKey]);
  if (!context) return children;
  return context.target ? createPortal(children, context.target) : null;
}
