// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { NavigationGuard, RouteLocationRaw } from 'vue-router';

import { channelAccessOptions } from '@/api/access';
import {
  channelDirectoryOptions,
  moderatedChannelsQuery,
  myChannelsQuery,
} from '@/api/channels';
import { ensureQuery } from '@/api/queryClient';
import { channelChoices } from '@/composables/useCurrentChannel';
import { heldManagementPage } from '@/layouts/dashboard/heldManagementPage';

import { channelParam } from './channelRoutes';

type Named = { channelId: string; onBot: boolean };

async function ownChannelId(login: string): Promise<string | null> {
  const [mine, moderated] = await Promise.all([
    ensureQuery(myChannelsQuery),
    ensureQuery(moderatedChannelsQuery),
  ]);

  return channelChoices(mine, moderated).find((row) => row.login.toLowerCase() === login)
    ?.channelId ?? null;
}

// Any channel by its login: the person's own and moderated lists first, then the public directory.
async function resolveChannel(login: string): Promise<Named | null> {
  const own = await ownChannelId(login);

  if (own !== null)
    return { channelId: own, onBot: true };

  const rows = await ensureQuery(channelDirectoryOptions(login));
  const found = rows.find((row) => row.login.toLowerCase() === login);

  return found === undefined
    ? null
    : { channelId: found.id, onBot: found.onBot };
}

function viewerShell(login: string, named: Named): RouteLocationRaw {
  return named.onBot
    ? {
        name: 'viewer-me',
        params: { channel: login },
        replace: true,
      }
    : {
        name: 'viewer-external-chat',
        params: { channel: login },
        query: { id: named.channelId },
        replace: true,
      };
}

// The shell is chosen per channel from the held action keys: one or more management pages held keeps the
// operator shell (/app/u/x); none sends the person to the viewer shell (/me/x), and a channel the bot
// does not serve goes to its chat (/me/x/twitch). A channel that cannot be found or read is left alone,
// so a missing channel still gets the no-access screen and the server still refuses every call.
export function rungGuard(): NavigationGuard {
  return async (to) => {
    const raw = to.params[channelParam];

    if (typeof raw !== 'string')
      return true;

    const login = raw.toLowerCase();

    try {
      const named = await resolveChannel(login);

      if (named === null)
        return true;

      if (!named.onBot)
        return viewerShell(raw, named);

      const access = await ensureQuery(channelAccessOptions(named.channelId));

      return heldManagementPage(new Set<string>(access.heldActionKeys))
        ? true
        : viewerShell(raw, named);
    } catch {
      return true;
    }
  };
}
