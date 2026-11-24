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
// One Twitch channel a name search found.
export type ChannelSearch = components['schemas']['ChannelSearch'];

// How many suggestions a channel search shows.
const CHANNEL_SEARCH_LIMIT = 8;

const version = { path: { version: '1' } };
// The server's largest page, and a ceiling so a list that never ends cannot loop forever.
const pageSize = 100;
const maxPages = 20;

// GET /channels declares Channel in the OpenAPI document but returns ChannelSummary rows
// (checked against the running bot, 2026-10-13), so the rows are read as the summary.
// Every page is read: the server pages this list, and a channel past the first page must stay reachable.
async function fetchMyChannels(): Promise<ChannelSummary[]> {
  const rows: ChannelSummary[] = [];

  for (let page = 1; page <= maxPages; page += 1) {
    const { data, error, response } = await client.GET('/api/v{version}/channels', {
      params: { ...version, query: { Page: page, PageSize: pageSize } },
    });

    if (!response.ok)
      throw problemError('my channels', response.status, error);

    // eslint-disable-next-line no-restricted-syntax -- the document types the rows as Channel, so the cast must go through the wire's true type
    rows.push(...((data?.data ?? []) as unknown as ChannelSummary[]));

    if (data?.hasMore !== true) break;
  }

  return rows;
}

async function fetchModeratedChannels(): Promise<ModeratedChannel[]> {
  const { data, error, response } = await client.GET('/api/v{version}/channels/moderated', {
    params: version,
  });

  if (!response.ok)
    throw problemError('moderated channels', response.status, error);

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

export function channelDirectoryOptions(search: string) {
  return queryOptions({
    queryKey: channelDirectoryKey(search),
    queryFn: async (): Promise<PublicChannel[]> => {
      const { data } = await client.GET('/api/v{version}/channels/directory', {
        params: {
          path: { version: '1' },
          query: {
            Search: search,
            PageSize: DIRECTORY_PAGE_SIZE,
          },
        },
      });

      return data?.data ?? [];
    },
    staleTime: 30_000,
  });
}

// Every channel this bot serves whose name holds the search, live first.
export function useChannelDirectory(
  search: MaybeRefOrGetter<string>,
): UseQueryReturnType<PublicChannel[], Error> {
  return useQuery(computed(() => channelDirectoryOptions(toValue(search))));
}

// Twitch channels whose name starts like the search, for a raid, invite or trust target. Empty text asks nothing.
export function useChannelSearch(
  channelId: MaybeRefOrGetter<string | null>,
  search: MaybeRefOrGetter<string>,
): UseQueryReturnType<ChannelSearch[], Error> {
  return useQuery({
    queryKey: computed(() => ['channels', toValue(channelId), 'search', toValue(search).trim()] as const),
    queryFn: async (): Promise<ChannelSearch[]> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/channels/{channelId}/stream/channels',
        {
          params: {
            path: { version: '1', channelId: toValue(channelId)! },
            query: { query: toValue(search).trim(), take: CHANNEL_SEARCH_LIMIT },
          },
        },
      );

      if (error !== undefined)
        throw problemError('channel search', response.status, error);

      return data?.data ?? [];
    },
    enabled: computed(() => toValue(channelId) !== null && toValue(search).trim() !== ''),
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
