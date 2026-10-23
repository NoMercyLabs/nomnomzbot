// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { ref } from 'vue';

import { queryClient } from './queryClient';

// The channel the page in front of the person is for. Most calls name it in their path, but some lists have no
// channel in the path (scripts, giveaways, quotes, assets, audio mix, data sources): the bot then answers for
// the caller's own channel unless the call carries this header. The client sends it on every call, so a person
// who switches to another channel never reads or writes their own channel's data by mistake.
export const channelHeader = 'X-Channel-Id';

export const activeChannelId = ref<string | null>(null);

// Follows the channel of the page being opened. A change drops every cached answer: the cache holds the last
// channel's data, and a page must never show it for the new one. The first channel known has nothing to drop.
export async function followChannel(channelId: string | null): Promise<void> {
  if (channelId === activeChannelId.value) return;

  const switched = activeChannelId.value !== null;

  activeChannelId.value = channelId;

  if (switched) {
    await queryClient.resetQueries();
  }
}
