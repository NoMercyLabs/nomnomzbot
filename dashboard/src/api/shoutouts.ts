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
  type QueryClient,
  type UseQueryReturnType,
} from '@tanstack/vue-query';
import { toValue, type MaybeRefOrGetter } from 'vue';

import { channelQuery, failed } from './channelQuery';
import { client } from './client';
import { viewerProfilesKey } from './people';
import type { components } from './schema';
import type { KeyParts } from '@/lib/array';

export type ShoutoutSuggestion = components['schemas']['ShoutoutSuggestionDto'];

const version = '1';

export const shoutoutSuggestionsKey = (channelId: string | null): KeyParts => [
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
    throw failed('shoutout suggestions', response.status, error);

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
) {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async (twitchUserId: string): Promise<void> => {
      const id = toValue(channelId) as string;
      const { error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/moderation/shoutout-suggestions/{twitchUserId}/dismiss',
        { params: { path: {
          version,
          channelId: id,
          twitchUserId,
        } } },
      );

      if (!response.ok)
        throw failed('dismiss suggestion', response.status, error);
    },
    onSuccess: () =>
      queries.invalidateQueries({ queryKey: shoutoutSuggestionsKey(toValue(channelId)) }),
  });
}

export type ShoutoutLine = {
  targetTwitchUserId: string;
  targetDisplayName: string;
  messageTemplate: string;
};

// The new line shows on the person's profile once the profile is read again.
function profilesChanged(queries: QueryClient, channelId: string | null): void {
  void queries.invalidateQueries({ queryKey: viewerProfilesKey(channelId) });
}

// This channel's own shoutout line for one person; the shoutout and a raid from them both use it.
export function useSetShoutoutLine(channelId: MaybeRefOrGetter<string | null>) {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async (line: ShoutoutLine): Promise<void> => {
      const { error, response } = await client.PUT(
        '/api/v{version}/channels/{channelId}/moderation/shoutout-overrides',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: line,
        },
      );

      if (!response.ok)
        throw failed('set shoutout line', response.status, error);
    },
    onSuccess: () =>
      profilesChanged(queries, toValue(channelId)),
  });
}

// Without the line, a shoutout falls back to the channel default.
export function useRemoveShoutoutLine(channelId: MaybeRefOrGetter<string | null>) {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async (targetTwitchUserId: string): Promise<void> => {
      const { error, response } = await client.DELETE(
        '/api/v{version}/channels/{channelId}/moderation/shoutout-overrides/{targetTwitchUserId}',
        { params: { path: {
          version,
          channelId: toValue(channelId) as string,
          targetTwitchUserId,
        } } },
      );

      if (!response.ok)
        throw failed('remove shoutout line', response.status, error);
    },
    onSuccess: () =>
      profilesChanged(queries, toValue(channelId)),
  });
}
