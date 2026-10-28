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

import { getLocalTimeZone, today } from '@internationalized/date';
import { useQuery, type UseQueryReturnType } from '@tanstack/vue-query';
import { computed, toValue, type MaybeRefOrGetter } from 'vue';

import { failed } from './channelQuery';
import { client } from './client';
import type { components } from './schema';

export type AnalyticsSummary = components['schemas']['ChannelAnalyticsSummary'];
export type AnalyticsDay = components['schemas']['ChannelAnalyticsDaily'];
export type StreamRow = components['schemas']['StreamListItem'];
export type ChatHours = components['schemas']['ChannelChatHours'];
export type ChatHourCell = components['schemas']['ChatHourCell'];

// The ranges the page offers, as the number of days back from today.
export const ANALYTICS_RANGES = {
  r30: 30,
  r90: 90,
  r365: 365,
} as const satisfies Record<string, number>;

export type AnalyticsRange = keyof typeof ANALYTICS_RANGES;

export type RangeDates = { from: string; to: string };

const version = '1';
const streamPageSize = 50;

// Calendar dates in the viewer's own time zone; the server reads them as whole days.
export function rangeDates(range: AnalyticsRange) {
  const end = today(getLocalTimeZone());
  const start = end.subtract({ days: ANALYTICS_RANGES[range] - 1 });

  return { from: start.toString(), to: end.toString() } as const satisfies RangeDates;
}

const analyticsKey = (channelId: string | null, what: string, range: AnalyticsRange) => [
  'analytics',
  channelId,
  what,
  range,
];

function rangeQuery<T>(
  channelId: MaybeRefOrGetter<string | null>,
  range: MaybeRefOrGetter<AnalyticsRange>,
  what: string,
  fetch: (channelId: string, dates: RangeDates) => Promise<T>,
): UseQueryReturnType<T, Error> {
  return useQuery({
    queryKey: computed(() => analyticsKey(toValue(channelId), what, toValue(range))),
    queryFn: (): Promise<T> => {
      const dates = rangeDates(toValue(range));

      return fetch(toValue(channelId) as string, dates);
    },
    enabled: computed(() => toValue(channelId) !== null),
  });
}

// The overview tab's reads for one range: what the page runs first and what a prefetch warms.
export function analyticsOverviewOptions(channelId: string, range: AnalyticsRange) {
  const dates = rangeDates(range);

  return [
    {
      queryKey: analyticsKey(channelId, 'summary', range),
      queryFn: () => readSummary(channelId, dates),
    },
    {
      queryKey: analyticsKey(channelId, 'daily', range),
      queryFn: () => readDays(channelId, dates),
    },
    {
      queryKey: analyticsKey(channelId, 'streams', range),
      queryFn: () => readStreams(channelId, dates),
    },
  ] as const;
}

async function readSummary(channelId: string, dates: RangeDates): Promise<AnalyticsSummary | null> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/analytics/channel/summary',
    { params: { path: { version, channelId }, query: dates } },
  );

  if (error !== undefined)
    throw failed('analytics summary', response.status, error);

  return data?.data ?? null;
}

async function readDays(channelId: string, dates: RangeDates): Promise<AnalyticsDay[]> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/analytics/channel/daily',
    { params: { path: { version, channelId }, query: dates } },
  );

  if (error !== undefined)
    throw failed('analytics daily', response.status, error);

  return [...(data?.data ?? [])];
}

async function readChatHours(channelId: string, dates: RangeDates): Promise<ChatHours | null> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/analytics/channel/chat-hours',
    { params: { path: { version, channelId }, query: dates } },
  );

  if (error !== undefined)
    throw failed('analytics chat hours', response.status, error);

  return data?.data ?? null;
}

// The list comes newest first, so paging stops at the first page that reaches back past the range.
async function readStreams(channelId: string, dates: RangeDates): Promise<StreamRow[]> {
  const rows: StreamRow[] = [];

  for (let page = 1; ; page++) {
    const { data, error, response } = await client.GET(
      '/api/v{version}/channels/{channelId}/analytics/streams',
      { params: { path: { version, channelId }, query: { Page: page, PageSize: streamPageSize } } },
    );

    if (error !== undefined)
      throw failed('analytics streams', response.status, error);

    const batch = data?.data ?? [];
    const inRange = batch.filter((row) => (row.startedAt ?? '').slice(0, 10) >= dates.from);

    rows.push(...inRange);

    if (!data?.hasMore || inRange.length < batch.length)
      return rows;
  }
}

export function useAnalyticsSummary(
  channelId: MaybeRefOrGetter<string | null>,
  range: MaybeRefOrGetter<AnalyticsRange>,
): UseQueryReturnType<AnalyticsSummary | null, Error> {
  return rangeQuery(channelId, range, 'summary', readSummary);
}

export function useAnalyticsDays(
  channelId: MaybeRefOrGetter<string | null>,
  range: MaybeRefOrGetter<AnalyticsRange>,
): UseQueryReturnType<AnalyticsDay[], Error> {
  return rangeQuery(channelId, range, 'daily', readDays);
}

export function useStreamsInRange(
  channelId: MaybeRefOrGetter<string | null>,
  range: MaybeRefOrGetter<AnalyticsRange>,
): UseQueryReturnType<StreamRow[], Error> {
  return rangeQuery(channelId, range, 'streams', readStreams);
}

// Chat lines per weekday and hour, in the channel's own time zone; an hour without chat is left out.
export function useChatHours(
  channelId: MaybeRefOrGetter<string | null>,
  range: MaybeRefOrGetter<AnalyticsRange>,
): UseQueryReturnType<ChatHours | null, Error> {
  return rangeQuery(channelId, range, 'chat-hours', readChatHours);
}
