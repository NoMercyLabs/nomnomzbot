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

import type { QueryKey } from '@tanstack/vue-query';
import { ref } from 'vue';

import { queryClient } from './queryClient';

// The channel the page in front of the person is for. Most calls name it in their path, but some lists have no
// channel in the path (scripts, giveaways, quotes, assets, audio mix, data sources): the bot then answers for
// the caller's own channel unless the call carries this header. The client sends it on every call, so a person
// who switches to another channel never reads or writes their own channel's data by mistake.
export const channelHeader = 'X-Channel-Id';

export const activeChannelId = ref<string | null>(null);

// The person's own + moderated channel lists (useCurrentChannel's `channels`) never change with which
// channel is being managed, so a switch must never drop them: useCurrentChannel resolves the picked
// channel's id from this same cache, and an empty cache resolves to no channel at all for the moment it
// takes to refetch. A query still enabled at that moment then runs with a null channel id, and the bot's
// OpenAPI client leaves an unresolved path like "/channels/{channelId}/..." literal instead of skipping
// the call (Stoney 2026-11-05: picking CheeseTheKenku fired a burst of literal-"{channelId}" requests,
// then fell back to his own channel once the lists came back under the still-old chosen channel).
function isChannelIdentityQuery(queryKey: QueryKey): boolean {
  return queryKey.length === 2 && queryKey[0] === 'channels';
}

// Follows the channel of the page being opened. A change drops every cached answer but the channel
// lists themselves: the cache holds the last channel's data, and a page must never show it for the new
// one. The first channel known has nothing to drop.
export async function followChannel(channelId: string | null): Promise<void> {
  if (channelId === activeChannelId.value) return;

  const switched = activeChannelId.value !== null;

  activeChannelId.value = channelId;

  if (switched) {
    await queryClient.resetQueries({
      predicate: (query) => !isChannelIdentityQuery(query.queryKey),
    });
  }
}
