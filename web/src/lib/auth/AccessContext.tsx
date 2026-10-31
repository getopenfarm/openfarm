import { createContext, useContext } from 'react';

// Presentation guards supplement server authorization. Undefined permits isolated component previews.
export const AccessContext = createContext<ReadonlySet<string> | undefined>(
  undefined,
);
export function useAllowed(permission?: string) {
  const access = useContext(AccessContext);
  return (
    !permission ||
    access === undefined ||
    permission
      .split(',')
      .every((p) => p.split('|').some((key) => access.has(key)))
  );
}
