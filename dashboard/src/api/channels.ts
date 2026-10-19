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
  queryOptions,
  useMutation,
  useQuery,
  useQueryClient,
  type UseQueryReturnType,
} from '@tanstack/vue-query';
import { computed, toValue, type MaybeRefOrGetter } from 'vue';

import type { components } from './schema';
import { client } from './client';
import { problemError } from './errors';

export type ChannelSummary = components['schemas']['ChannelSummary'];
export type ModeratedChannel = components['schemas']['ModeratedChannel'];
// Why and since when the platform suspended a channel, and where its people reach the platform.
export type ChannelSuspension = components['schemas']['ChannelSuspension'];
// Any channel this bot serves, with what every visitor already sees (chat-client.md 9a "Reaching any channel").
// onBot is false for a Twitch channel this deployment does not serve.
export type PublicChannel = components['schemas']['PublicChannel'];

const version = { path: { version: '1' } };

// GET /channels declares Channel in the OpenAPI document but returns ChannelSummary rows
// (checked against the running bot, 2026-10-13), so the rows are read as the summary.
async function fetchMyChannels(): Promise<ChannelSummary[]> {
  const { data, error, response } = await client.GET('/api/v{version}/channels', {
    params: version,
  });

  if (!response.ok) throw problemError('my channels', response.status, error);

  // eslint-disable-next-line no-restricted-syntax -- the document types the rows as Channel, so the cast must go through the wire's true type
  return (data?.data ?? []) as unknown as ChannelSummary[];
}

async function fetchModeratedChannels(): Promise<ModeratedChannel[]> {
  const { data, error, response } = await client.GET('/api/v{version}/channels/moderated', {
    params: version,
  });

  if (!response.ok) throw problemError('moderated channels', response.status, error);

  return data?.data ?? [];
}

export const myChannelsQuery = queryOptions({
  queryKey: ['channels', 'mine'],
  queryFn: fetchMyChannels,
});

export const moderatedChannelsQuery = queryOptions({
  queryKey: ['channels', 'moderated'],
  queryFn: fetchModeratedChannels,
});

export function useMyChannels(): UseQueryReturnType<ChannelSummary[], Error> {
  return useQuery(myChannelsQuery);
}

export function useModeratedChannels(): UseQueryReturnType<ModeratedChannel[], Error> {
  return useQuery(moderatedChannelsQuery);
}

export const DIRECTORY_PAGE_SIZE = 20;

export function channelDirectoryKey(search: string) {
  return ['channels', 'directory', search] as const;
}

// Every channel this bot serves whose name holds the search, live first.
export function useChannelDirectory(
  search: MaybeRefOrGetter<string>,
): UseQueryReturnType<PublicChannel[], Error> {
  return useQuery({
    queryKey: computed(() => channelDirectoryKey(toValue(search))),
    queryFn: async (): Promise<PublicChannel[]> => {
      const { data } = await client.GET('/api/v{version}/channels/directory', {
        params: {
          path: { version: '1' },
          query: {
            Search: toValue(search),
            PageSize: DIRECTORY_PAGE_SIZE,
          },
        },
      });

      return data?.data ?? [];
    },
    staleTime: 30_000,
  });
}

// A channel the person moderates on Twitch, where the bot is not installed yet, opens in moderator mode.
export function useEnterModeratedChannel() {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async (twitchBroadcasterId: string): Promise<ChannelSummary | null> => {
      const { data } = await client.POST(
        '/api/v{version}/channels/moderated/{twitchBroadcasterId}/enter',
        {
          params: { path: { version: '1', twitchBroadcasterId } },
        },
      );

      return data?.data ?? null;
    },
    onSuccess: () => queries.invalidateQueries({ queryKey: ['channels'] }),
  });
}
