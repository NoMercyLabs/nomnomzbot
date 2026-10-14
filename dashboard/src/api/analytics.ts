// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { getLocalTimeZone, today } from '@internationalized/date';
import { useQuery, type UseQueryReturnType } from '@tanstack/vue-query';
import { computed, toValue, type MaybeRefOrGetter } from 'vue';

import { failed } from './channelQuery';
import { client } from './client';
import type { components } from './schema';

export type AnalyticsSummary = components['schemas']['ChannelAnalyticsSummaryDto'];
export type AnalyticsDay = components['schemas']['ChannelAnalyticsDailyDto'];
export type StreamRow = components['schemas']['StreamListItemDto'];
export type ChatHours = components['schemas']['ChannelChatHoursDto'];
export type ChatHourCell = components['schemas']['ChatHourCellDto'];

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
export function rangeDates(range: AnalyticsRange): RangeDates {
  const end = today(getLocalTimeZone());
  const start = end.subtract({ days: ANALYTICS_RANGES[range] - 1 });

  return { from: start.toString(), to: end.toString() };
}

function rangeQuery<T>(
  channelId: MaybeRefOrGetter<string | null>,
  range: MaybeRefOrGetter<AnalyticsRange>,
  what: string,
  fetch: (channelId: string, dates: RangeDates) => Promise<T>,
): UseQueryReturnType<T, Error> {
  return useQuery({
    queryKey: computed(() => ['analytics', toValue(channelId), what, toValue(range)]),
    queryFn: (): Promise<T> => {
      const dates = rangeDates(toValue(range));

      return fetch(toValue(channelId) as string, dates);
    },
    enabled: computed(() => toValue(channelId) !== null),
  });
}

async function readSummary(channelId: string, dates: RangeDates): Promise<AnalyticsSummary | null> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/analytics/channel/summary',
    { params: { path: { version, channelId }, query: dates } },
  );

  if (error !== undefined)
    throw failed('analytics summary', response.status);

  return data?.data ?? null;
}

async function readDays(channelId: string, dates: RangeDates): Promise<AnalyticsDay[]> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/analytics/channel/daily',
    { params: { path: { version, channelId }, query: dates } },
  );

  if (error !== undefined)
    throw failed('analytics daily', response.status);

  return [...(data?.data ?? [])];
}

async function readChatHours(channelId: string, dates: RangeDates): Promise<ChatHours | null> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/analytics/channel/chat-hours',
    { params: { path: { version, channelId }, query: dates } },
  );

  if (error !== undefined)
    throw failed('analytics chat hours', response.status);

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
      throw failed('analytics streams', response.status);

    const batch = data?.data ?? [];
    const inRange = batch.filter((row: StreamRow) => (row.startedAt ?? '').slice(0, 10) >= dates.from);

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
