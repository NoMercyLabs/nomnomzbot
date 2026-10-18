// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { NavigationGuard, RouteLocationNormalized, RouteLocationRaw, Router } from 'vue-router';

import { channelAccessOptions } from '@/api/access';
import { moderatedChannelsQuery, myChannelsQuery } from '@/api/channels';
import { queryClient } from '@/api/queryClient';
import { channelChoices, chosenChannelId, pickCurrent } from '@/composables/useCurrentChannel';
import { dashboardNav } from '@/layouts/dashboard/navigation';
import { merge } from '@/lib/array';

import { channelParam } from './channelRoutes';

const variantMark = 'u:';
const homePage = 'dashboard-home';
const viewerChatPage = 'dashboard-viewer-chat';
const viewerChannelParam = 'channel';
const viewerPage = 'viewer-me';

// The channel login the address names: /app/u/:channelName or a viewer page under /me/:channel.
function namedChannel(to: RouteLocationNormalized): string | undefined {
  const manage = to.params[channelParam];
  const viewer = to.params[viewerChannelParam];

  if (typeof manage === 'string')
    return manage;

  return typeof viewer === 'string'
    ? viewer
    : undefined;
}

// The channel the target page is for: the one named in the address, else the one being managed.
// Null when the channel is not set up in this bot, so no access can be read for it.
async function targetChannelId(to: RouteLocationNormalized): Promise<string | null> {
  const [mine, moderated] = await Promise.all([
    queryClient.ensureQueryData(myChannelsQuery),
    queryClient.ensureQueryData(moderatedChannelsQuery),
  ]);
  const choices = channelChoices(mine, moderated);
  const named = namedChannel(to);

  if (named === undefined)
    return pickCurrent(choices, chosenChannelId.value)?.channelId ?? null;

  const asked = named.toLowerCase();

  return choices.find((row) => row.login.toLowerCase() === asked)?.channelId ?? null;
}

// Home first, then the viewer chat, then the rest of the sidebar in its own order.
function fallbackPages(): readonly { route: string; action?: string }[] {
  const entries = dashboardNav.flatMap((group) => group.entries);
  const first = [homePage, viewerChatPage];
  const preferred = first.flatMap((route) => entries.filter((entry) => entry.route === route));
  const rest = entries.filter((entry) => !first.includes(entry.route));

  return merge(preferred, rest);
}

function opensFor(router: Router, held: ReadonlySet<string>, location: RouteLocationRaw): boolean {
  const target = router.resolve(location);
  const needed = target.meta.action;

  return needed === undefined || held.has(needed);
}

// The first page the person may open, in the channel the refused page was for.
function firstOpenPage(
  router: Router,
  to: RouteLocationNormalized,
  held: ReadonlySet<string>,
): RouteLocationRaw | true {
  const named = to.params[channelParam];
  const viewing = to.params[viewerChannelParam];

  if (typeof named !== 'string' && typeof viewing === 'string') {
    return {
      name: viewerPage,
      params: { [viewerChannelParam]: viewing },
      replace: true,
    };
  }

  for (const entry of fallbackPages()) {
    const name = typeof named === 'string'
      ? `${variantMark}${entry.route}`
      : entry.route;
    const location: RouteLocationRaw = typeof named === 'string'
      ? {
          name,
          params: { [channelParam]: named },
          replace: true,
        }
      : { name, replace: true };
    const allowed = entry.action === undefined || held.has(entry.action);

    if (allowed && router.hasRoute(name) && opensFor(router, held, location))
      return location;
  }

  return true;
}

// A page that names meta.action opens only for a person who holds that action key in the target
// channel; anyone else lands on the first page they may open. A channel or access that cannot be
// read keeps the page, because the server still refuses every call the page makes.
export function accessGuard(router: Router): NavigationGuard {
  return async (to) => {
    const action = to.meta.action;

    if (action === undefined)
      return true;

    try {
      const channelId = await targetChannelId(to);

      if (channelId === null)
        return true;

      const access = await queryClient.ensureQueryData(channelAccessOptions(channelId));
      const held = new Set<string>(access.heldActionKeys);

      if (held.has(action))
        return true;

      return firstOpenPage(router, to, held);
    } catch {
      return true;
    }
  };
}
