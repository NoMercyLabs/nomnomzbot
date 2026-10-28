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

import { describe, expect, it, vi } from 'vitest';
import { ref } from 'vue';

import type { CurrentUser } from '@/api/auth';

import { holdsPlatformKey, usePlatformCan } from '../usePlatformCan';

const signedIn = vi.hoisted(() => ({ user: null as unknown }));

vi.mock('@/api/auth', () => ({
  useCurrentUser: () => ({ data: signedIn.user }),
}));

function person(isAdmin: boolean, platformPermissions: string[]): CurrentUser {
  return {
    id: 'u1',
    username: 'someone',
    displayName: 'Someone',
    broadcasterType: '',
    isAdmin,
    createdAt: '2026-01-01T00:00:00Z',
    platformPermissions,
  };
}

describe('holdsPlatformKey', () => {
  it('is true for an admin who holds the key and false for any other key', () => {
    const admin = person(true, ['billing:read']);

    expect(holdsPlatformKey(admin, 'billing:read')).toBe(true);
    expect(holdsPlatformKey(admin, 'billing:write')).toBe(false);
  });

  it('is false for a person who is not an admin, even with the key in the list', () => {
    expect(holdsPlatformKey(person(false, ['billing:read']), 'billing:read')).toBe(false);
  });

  it('is false when nobody is signed in', () => {
    expect(holdsPlatformKey(null, 'billing:read')).toBe(false);
    expect(holdsPlatformKey(undefined, 'billing:read')).toBe(false);
  });
});

describe('usePlatformCan', () => {
  it('is unknown and refuses every key until the bot has answered', () => {
    signedIn.user = ref(undefined);
    const platform = usePlatformCan();

    expect(platform.known.value).toBe(false);
    expect(platform.can('billing:read')).toBe(false);
  });

  it('follows the signed-in person once the bot has answered', () => {
    const user = ref<CurrentUser | undefined>(undefined);

    signedIn.user = user;
    const platform = usePlatformCan();

    user.value = person(true, ['billing:read']);

    expect(platform.known.value).toBe(true);
    expect(platform.can('billing:read')).toBe(true);
    expect(platform.can('iam:manage')).toBe(false);
  });
});
