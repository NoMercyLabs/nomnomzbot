// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { NavigationGuard } from 'vue-router';

import { followChannel } from '@/api/activeChannel';

import { targetChannelId } from './accessGuard';

// Tells the client which channel the page being opened is for (the one named in the address, else the one being
// managed), so every call of that page, even a list with no channel in its path, answers for that channel. The
// channel cannot be read for a page that is not signed in, or when the channel lists are down: the page then
// opens as before and the bot still refuses what the person may not touch.
export const channelScopeGuard: NavigationGuard = async (to) => {
  if (!to.matched.some((route) => route.meta.requiresAuth))
    return true;

  try {
    await followChannel(await targetChannelId(to));
  } catch {
    return true;
  }

  return true;
};
