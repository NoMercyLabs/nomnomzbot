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
  useInfiniteQuery,
  type InfiniteData,
  type UseInfiniteQueryReturnType,
} from '@tanstack/vue-query';
import { computed, toValue, type MaybeRefOrGetter } from 'vue';

import { numberOrNull } from '@/lib/number';

import { failed } from './channelQuery';
import { client } from './client';
import type { components } from './schema';

export type ModerationHistoryEntry = components['schemas']['ModerationHistoryEntryDto'];

export type ModerationHistoryPage = {
  entries: ModerationHistoryEntry[];
  hasMore: boolean;
  nextPage: number | null;
};

const version = '1';
const pageSize = 25;
const firstPage = 1;

export const moderationHistoryKey = (
  channelId: string | null,
  userId: string | null,
): unknown[] => ['moderation', channelId, 'history', userId];

async function readHistoryPage(
  channelId: string,
  userId: string,
  page: number,
): Promise<ModerationHistoryPage> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/moderation/history/{userId}',
    {
      params: {
        path: {
          version,
          channelId,
          userId,
        },
        query: { Page: page, PageSize: pageSize },
      },
    },
  );

  if (error !== undefined)
    throw failed('moderation history', response.status);

  return {
    entries: data?.data ?? [],
    hasMore: data?.hasMore ?? false,
    nextPage: numberOrNull(data?.nextPage),
  };
}

// One person's moderation history in this channel, newest first. The id is the internal one
// (CommunityRow.internalUserId); a null id, such as a caller without the read right, reads nothing.
export function useModerationHistory(
  channelId: MaybeRefOrGetter<string | null>,
  userId: MaybeRefOrGetter<string | null>,
): UseInfiniteQueryReturnType<InfiniteData<ModerationHistoryPage, number>, Error> {
  return useInfiniteQuery({
    queryKey: computed(() => moderationHistoryKey(toValue(channelId), toValue(userId))),
    queryFn: ({ pageParam }: { pageParam: number }): Promise<ModerationHistoryPage> =>
      readHistoryPage(toValue(channelId) as string, toValue(userId) as string, pageParam),
    initialPageParam: firstPage,
    getNextPageParam: (
      last: ModerationHistoryPage,
      all: ModerationHistoryPage[],
    ): number | undefined =>
      last.hasMore
        ? (last.nextPage ?? all.length + 1)
        : undefined,
    enabled: computed(() => toValue(channelId) !== null && toValue(userId) !== null),
  });
}
