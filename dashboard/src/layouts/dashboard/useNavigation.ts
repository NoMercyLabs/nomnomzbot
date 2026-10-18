// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { useStorage } from '@vueuse/core';
import { computed, watch, type ComputedRef } from 'vue';
import { useRoute, useRouter, type RouteLocationRaw } from 'vue-router';

import { useChannelAccess } from '@/api/access';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePlatformCan } from '@/composables/usePlatformCan';
import { isChannelPage, pageLocation, pageName, routeChannel } from '@/router/channelRoutes';
import { adminPageKey } from '@/views/dashboard/admin/adminKeys';

import { navByMode } from './navModes';
import type { NavEntry, NavGroup, NavMode } from './navigation';

export type VisibleGroup = {
  key: string;
  entries: NavEntry[];
  // A group of one page has nothing to fold.
  foldable: boolean;
  open: boolean;
  holdsOpenPage: boolean;
};

export type Navigation = {
  groups: ComputedRef<VisibleGroup[]>;
  entries: ComputedRef<NavEntry[]>;
  isCurrent: (entry: NavEntry) => boolean;
  // Where the entry's link goes: the page under the channel prefix.
  linkTo: (entry: NavEntry) => RouteLocationRaw;
  toggle: (group: VisibleGroup) => void;
};

// Which groups are open: group key to true (open) or false (folded). It outlives a reload. Only the person folds a group.
const folds = useStorage<Record<string, boolean>>(
  'nnz.nav.folds',
  {},
);

export function useNavigation(): Navigation {
  const router = useRouter();
  const route = useRoute();
  const { channelId, current } = useCurrentChannel();
  const access = useChannelAccess(channelId);
  const platform = usePlatformCan();

  // An admin page the person holds no platform key for has no entry either.
  function holdsAdminKey(entry: NavEntry): boolean {
    const key = adminPageKey(entry.route);

    return key === undefined || !platform.known.value || platform.can(key);
  }

  // A page the person may not read has no entry. Until the bot has answered, nothing is hidden.
  function readable(entry: NavEntry): boolean {
    if (!holdsAdminKey(entry))
      return false;

    if (!access.known.value)
      return true;

    if (entry.unless !== undefined && access.can(entry.unless))
      return false;

    // The entry names its own action, else it takes the one its route names: one key for entry and route.
    const needed = entry.action ?? router.resolve({ name: entry.route }).meta.action;

    return needed === undefined || access.can(needed);
  }

  // A page that is not built yet has no route, so it has no entry (pages appear as they land).
  function isCurrent(entry: NavEntry): boolean {
    if (typeof route.name !== 'string')
      return false;

    const page = pageName(route.name);

    return page === entry.route || (entry.exact !== true && page.startsWith(`${entry.route}-`));
  }

  // The channel in the address while the person is on a channel route, else the one being managed.
  function linkTo(entry: NavEntry): RouteLocationRaw {
    if (!isChannelPage(router.resolve({ name: entry.route }).meta))
      return { name: entry.route };

    return pageLocation(entry.route, routeChannel(route) ?? current.value?.login ?? null);
  }

  // The open page says which sidebar it belongs to; a page that says nothing sits in the dashboard.
  const mode = computed<NavMode>(() => route.meta.navMode ?? 'dashboard');

  const groups = computed<VisibleGroup[]>(() =>
    navByMode[mode.value]
      .map((group: NavGroup): VisibleGroup => {
        const entries = group.entries.filter(
          (entry) => router.hasRoute(entry.route) && readable(entry),
        );
        const holdsOpenPage = entries.some(isCurrent);

        return {
          key: group.key,
          entries,
          foldable: entries.length > 1,
          open: entries.length <= 1 || holdsOpenPage || folds.value[group.key] === true,
          holdsOpenPage,
        };
      })
      .filter((group) => group.entries.length > 0),
  );

  // Opening a page opens its group and remembers that, so moving on to another group never folds it.
  watch(
    () => groups.value.find((group) => group.foldable && group.holdsOpenPage)?.key,
    (key: string | undefined) => {
      if (key !== undefined && folds.value[key] !== true) {
        folds.value = { ...folds.value, [key]: true };
      }
    },
    { immediate: true },
  );

  function toggle(group: VisibleGroup): void {
    if (!group.foldable || group.holdsOpenPage) return;

    folds.value = { ...folds.value, [group.key]: !group.open };
  }

  return {
    groups,
    entries: computed(() => groups.value.flatMap((group) => group.entries)),
    isCurrent,
    linkTo,
    toggle,
  };
}
