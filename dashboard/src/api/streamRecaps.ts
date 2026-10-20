// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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

const version = '1';

async function readPage(channelId: string, page: number): Promise<StreamRecapPage> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/dashboard/{channelId}/stream-recaps',
    { params: { path: { version, channelId }, query: { Page: page, PageSize: RECAP_PAGE_SIZE } } },
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

// Every kept recap of the channel, newest stream first, one page at a time.
export function useStreamRecaps(
  channelId: MaybeRefOrGetter<string | null>,
  page: MaybeRefOrGetter<number>,
): UseQueryReturnType<StreamRecapPage, Error> {
  return useQuery({
    queryKey: computed(() => ['stream-recaps', toValue(channelId), toValue(page)]),
    queryFn: (): Promise<StreamRecapPage> => readPage(toValue(channelId) as string, toValue(page)),
    enabled: computed(() => toValue(channelId) !== null),
  });
}
