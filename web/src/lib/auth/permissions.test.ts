import { describe, expect, it } from 'vitest';
import { routePermission, settingsPath } from './permissions';

describe('routePermission', () => {
  it('keeps /profile open without matching /profiles', () => {
    expect(routePermission('/profile')).toBeNull();
    expect(routePermission('/profiles')).toBe('profiles:list-machine');
  });

  it('gates maintenance reads on view and its forms on manage', () => {
    expect(routePermission('/maintenance')).toBe('maintenance:view');
    expect(routePermission('/maintenance/new')).toBe('maintenance:manage');
    expect(routePermission('/maintenance/t1/edit')).toBe('maintenance:manage');
  });
});

describe('settingsPath', () => {
  it('picks the first readable settings page', () => {
    expect(settingsPath(() => true)).toBe('/settings');
    expect(settingsPath((key) => key === 'settings:printer-costs:read')).toBe(
      '/settings/printer-costs',
    );
    expect(settingsPath(() => false)).toBeNull();
  });
});
