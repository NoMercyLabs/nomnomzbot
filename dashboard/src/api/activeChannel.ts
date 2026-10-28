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
