// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
export function usePlatformCan(): ChannelAccess {
  const user = useCurrentUser();

  return {
    known: computed(() => user.data.value !== undefined),
    can: (key) => holdsPlatformKey(user.data.value, key),
  };
}
