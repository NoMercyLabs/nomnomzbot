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

import { useQuery, type UseQueryReturnType } from '@tanstack/vue-query';
import { computed, toValue, type MaybeRefOrGetter } from 'vue';

import { client } from './client';
import type { components } from './schema';

// The kept recap of one stream. The output size fields are newer than the generated schema.
export type StreamRecap = components['schemas']['StreamRecap'] & {
  outputWidth?: number | null;
  outputHeight?: number | null;
};

export type StreamRecapPage = {
  recaps: StreamRecap[];
  total: number;
  hasMore: boolean;
};

export const RECAP_PAGE_SIZE = 12;

// A recap kept before the channel metric existed stores no channel; it belongs to the managed channel, so the
// streamer card and the thumbnail take that channel's name and picture.
export function withChannel(
  recap: StreamRecap,
  channel: { displayName: string; profileImageUrl: string | null } | null,
) {
  if (recap.stats.channel || !channel)
    return recap;

  const filled = { name: channel.displayName, avatarUrl: channel.profileImageUrl };

  return { ...recap, stats: { ...recap.stats, channel: filled } } as const satisfies StreamRecap;
}

// The most the server sends in one request (PaginationParams.MaxPageSize).
const FETCH_PAGE_SIZE = 100;

const version = '1';

async function readPage(channelId: string, page: number): Promise<StreamRecapPage> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/dashboard/{channelId}/stream-recaps',
    { params: { path: { version, channelId }, query: { Page: page, PageSize: FETCH_PAGE_SIZE } } },
  );

  if (error !== undefined)
    throw new Error(`stream recaps failed (${response.status})`);

  const recaps = (data?.data ?? []) as StreamRecap[];

  return {
    recaps,
    total: Number(data?.total ?? recaps.length),
    hasMore: data?.hasMore ?? false,
  };
}

// Sorting by any column needs the whole list, so the pages are read one after the other until the last one.
async function readAll(channelId: string): Promise<StreamRecapPage> {
  const recaps: StreamRecap[] = [];
  let page = 1;
  let last = await readPage(channelId, page);

  recaps.push(...last.recaps);

  while (last.hasMore) {
    page++;
    last = await readPage(channelId, page);
    recaps.push(...last.recaps);
  }

  return {
    recaps,
    total: recaps.length,
    hasMore: false,
  };
}

// Every kept recap of the channel, newest stream first.
export function useStreamRecaps(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<StreamRecapPage, Error> {
  return useQuery({
    queryKey: computed(() => ['stream-recaps', toValue(channelId)]),
    queryFn: (): Promise<StreamRecapPage> => readAll(toValue(channelId) as string),
    enabled: computed(() => toValue(channelId) !== null),
  });
}
