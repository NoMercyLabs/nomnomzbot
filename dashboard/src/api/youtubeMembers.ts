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

// The channel's YouTube members: what Twitch subscribers are, on YouTube. Read with the streamer's permission.
import {
  useInfiniteQuery,
  type InfiniteData,
  type UseInfiniteQueryReturnType,
} from '@tanstack/vue-query';
import { computed, toValue, type MaybeRefOrGetter } from 'vue';

import { client } from './client';
import { failed } from './errors';
import type { components } from './schema';
import type { KeyParts } from '@/lib/array';

export type YouTubeMembers = components['schemas']['YouTubeMembers'];
export type YouTubeMember = components['schemas']['YouTubeMember'];

type PageToken = string | null;

const version = '1';
const pageSize = 50;
// Google counts every read against the day's quota, so the list is not asked again on every visit.
const freshFor = 5 * 60_000;

export const youTubeMembersKey = (channelId: string | null): KeyParts => [
  'community',
  channelId,
  'youtube-members',
];

async function readPage(channelId: string, pageToken: PageToken): Promise<YouTubeMembers | null> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/community/youtube/members',
    {
      params: {
        path: { version, channelId },
        query: {
          take: pageSize,
          ...(pageToken === null
            ? {}
            : { pageToken }),
        },
      },
    },
  );

  if (error !== undefined)
    throw failed('youtube members', response.status, error);

  return data?.data ?? null;
}

// Pages of members in the order YouTube gives. `access` on the first page says why there are none.
export function useYouTubeMembers(
  channelId: MaybeRefOrGetter<string | null>,
): UseInfiniteQueryReturnType<InfiniteData<YouTubeMembers | null, PageToken>, Error> {
  return useInfiniteQuery({
    queryKey: computed(() => youTubeMembersKey(toValue(channelId))),
    queryFn: ({ pageParam }: { pageParam: PageToken }): Promise<YouTubeMembers | null> =>
      readPage(toValue(channelId) as string, pageParam),
    initialPageParam: null as PageToken,
    getNextPageParam: (last: YouTubeMembers | null): PageToken | undefined =>
      last?.nextPageToken ?? undefined,
    enabled: computed(() => toValue(channelId) !== null),
    staleTime: freshFor,
  });
}
