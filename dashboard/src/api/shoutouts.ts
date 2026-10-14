// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import {
  useMutation,
  useQueryClient,
  type UseMutationReturnType,
  type UseQueryReturnType,
} from '@tanstack/vue-query';
import { toValue, type MaybeRefOrGetter } from 'vue';

import { channelQuery, failed } from './channelQuery';
import { client } from './client';
import type { components } from './schema';

export type ShoutoutSuggestion = components['schemas']['ShoutoutSuggestionDto'];

const version = '1';

export const shoutoutSuggestionsKey = (channelId: string | null): unknown[] => [
  'channels',
  channelId,
  'shoutout-suggestions',
];

async function readSuggestions(channelId: string): Promise<ShoutoutSuggestion[]> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/moderation/shoutout-suggestions',
    { params: { path: { version, channelId } } },
  );

  if (error !== undefined)
    throw failed('shoutout suggestions', response.status);

  return data?.data ?? [];
}

// Chatters of this stream who went live often, most streams first (the server ranks them).
export function useShoutoutSuggestions(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<ShoutoutSuggestion[], Error> {
  return channelQuery(channelId, shoutoutSuggestionsKey, readSuggestions);
}

// "Not now": the chatter leaves the list for this stream.
export function useDismissShoutoutSuggestion(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, string, unknown> {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async (twitchUserId: string): Promise<void> => {
      const id = toValue(channelId) as string;
      const { response } = await client.POST(
        '/api/v{version}/channels/{channelId}/moderation/shoutout-suggestions/{twitchUserId}/dismiss',
        { params: { path: {
          version,
          channelId: id,
          twitchUserId,
        } } },
      );

      if (!response.ok)
        throw failed('dismiss suggestion', response.status);
    },
    onSuccess: () =>
      queries.invalidateQueries({ queryKey: shoutoutSuggestionsKey(toValue(channelId)) }),
  });
}
