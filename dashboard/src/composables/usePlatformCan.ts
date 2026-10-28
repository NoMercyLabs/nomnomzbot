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

import { computed } from 'vue';

import { useCurrentUser, type CurrentUser } from '@/api/auth';
import type { ChannelAccess } from '@/api/access';

// Does the person hold this platform key (iam:manage, billing:write, ...)? Only a platform admin can.
export function holdsPlatformKey(user: CurrentUser | null | undefined, key: string): boolean {
  return user?.isAdmin === true && user.platformPermissions.includes(key);
}

// The way to gate a platform admin element (a tab, panel or button): `const { can } = usePlatformCan()`,
// then `v-if="can('billing:write')"`. The keys come from the bot with the signed-in person (auth/me),
// the same keys its admin endpoints check. Until the bot has answered, `can` is false for every key.
export function usePlatformCan() {
  const user = useCurrentUser();

  return {
    known: computed(() => user.data.value !== undefined),
    can: (key) => holdsPlatformKey(user.data.value, key),
  } as const satisfies ChannelAccess;
}
