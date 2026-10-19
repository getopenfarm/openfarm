import { Navigate, useLocation } from 'react-router';
export function LegacyCostCatalogRedirect() {
  const location = useLocation();
  return (
    <Navigate
      replace
      to={`${location.pathname.replace(/^\/non-printed-components/, '/cost-catalog')}${location.search}${location.hash}`}
    />
  );
}
