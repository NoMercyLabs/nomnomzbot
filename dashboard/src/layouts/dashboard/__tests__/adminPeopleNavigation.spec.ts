// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

import { describe, expect, it } from 'vitest';

import { adminPageKey } from '@/views/dashboard/admin/adminKeys';
import Users from '~icons/nnz/users';

import { adminNav } from '../adminNavigation';
import { pageLabel } from '../pageLabels';

const people = adminNav.find((group) => group.key === 'admin.people')!;
const routes = adminNav.flatMap((group) => group.entries.map((entry) => entry.route));

describe('the People group of the admin sidebar', () => {
  it('lists one Tenants entry, with the Users icon, and the support entry beside it', () => {
    expect(people.entries.map((entry) => entry.route)).toEqual([
      'dashboard-admin-tenants',
      'dashboard-admin-support',
    ]);

    const tenants = people.entries[0]!;

    expect(tenants.label).toBe('admin.tenants');
    expect(tenants.icon).toBe(Users);
  });

  it('has no separate Channels or Users entry: a person and their channel are one tenant row', () => {
    expect(routes).not.toContain('dashboard-admin-channels');
    expect(routes).not.toContain('dashboard-admin-users');

    const labels = people.entries.map((entry) => entry.label);

    expect(labels).not.toContain('admin.channels');
    expect(labels).not.toContain('admin.users');
  });

  it('needs the tenant read key, the key the list endpoint checks', () => {
    expect(adminPageKey('dashboard-admin-tenants')).toBe('tenant:read');
    expect(adminPageKey('dashboard-admin-channels-console')).toBe('tenant:read');
  });

  it('keeps no key for the two routes that no longer exist', () => {
    expect(adminPageKey('dashboard-admin-channels')).toBeUndefined();
    expect(adminPageKey('dashboard-admin-users')).toBeUndefined();
  });

  it('names the channel console after the Tenants page it opens from', () => {
    expect(pageLabel('dashboard-admin-channels-console')).toBe(pageLabel('dashboard-admin-tenants'));
    expect(pageLabel('dashboard-admin-tenants')).toBe('admin.tenants');
  });
});
