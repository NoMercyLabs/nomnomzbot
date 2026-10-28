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
  useMutation,
  useQueryClient,
  type UseMutationReturnType,
  type UseQueryReturnType,
} from '@tanstack/vue-query';
import { toValue, type MaybeRefOrGetter } from 'vue';

import type { KeyParts } from '@/lib/array';

import { channelQuery, failed, refusal } from './channelQuery';
import { client } from './client';
import type { components } from './schema';

// A custom event feed is a custom data source of the API: an outside source whose data becomes a `custom.<name>` event.
export type EventFeed = components['schemas']['CustomDataSource'];
export type EventFeedDraft = components['schemas']['UpsertCustomDataSourceRequest'];
export type EventFeedPreset = components['schemas']['CustomDataSourcePreset'];
export type EventFeedFetch = components['schemas']['CustomDataSourceTestFetch'];
export type EventFeedBlastRadius = components['schemas']['BlastRadius'];

export const eventFeedKinds = ['poll', 'socket', 'push'] as const;

const version = '1';
const pageSize = 100;
const maxPages = 20;

export const eventFeedsKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'custom-event-feeds',
];

export const eventFeedPresetsKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'custom-event-feed-presets',
];

async function readFeeds(): Promise<EventFeed[]> {
  const rows: EventFeed[] = [];

  for (let page = 1; page <= maxPages; page += 1) {
    const { data, error, response } = await client.GET('/api/v{version}/custom-data-sources', {
      params: { path: { version }, query: { Page: page, PageSize: pageSize } },
    });

    if (error !== undefined)
      throw failed('custom event feeds', response.status, error);

    rows.push(...(data?.data ?? []));

    if (data?.hasMore !== true) break;
  }

  return rows;
}

async function readPresets(): Promise<EventFeedPreset[]> {
  const { data, error, response } = await client.GET('/api/v{version}/custom-data-sources/presets', {
    params: { path: { version } },
  });

  const rows = data?.data;

  if (error !== undefined || rows === undefined || rows === null)
    throw failed('custom event feed presets', response.status, error);

  return rows;
}

// The bot publishes CustomDataSourceChanged for every create, edit, delete and received datum (realtime/liveSync.ts).
export function useEventFeeds(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<EventFeed[], Error> {
  return channelQuery(channelId, eventFeedsKey, readFeeds);
}

export function useEventFeedPresets(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<EventFeedPreset[], Error> {
  return channelQuery(channelId, eventFeedPresetsKey, readPresets);
}

export type SaveEventFeed = { id: string | null; draft: EventFeedDraft };

// No id creates a feed; an id replaces the feed with the draft.
export function useSaveEventFeed(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<EventFeed, Error, SaveEventFeed, undefined> {
  const queries = useQueryClient();

  return useMutation<EventFeed, Error, SaveEventFeed, undefined>({
    mutationFn: async ({ id, draft }: SaveEventFeed): Promise<EventFeed> => {
      const answer = id === null
        ? await client.POST('/api/v{version}/custom-data-sources', {
            params: { path: { version } },
            body: draft,
          })
        : await client.PUT('/api/v{version}/custom-data-sources/{id}', {
            params: { path: { version, id } },
            body: draft,
          });

      const saved = answer.data?.data;

      if (answer.error !== undefined || saved === undefined || saved === null)
        throw refusal('save custom event feed', answer.error, answer.response.status);

      return saved;
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: eventFeedsKey(toValue(channelId)) });
    },
  });
}

// The blast radius is counted by the bot: how many event responses and widgets read this feed.
export async function fetchEventFeedBlastRadius(id: string): Promise<EventFeedBlastRadius> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/custom-data-sources/{id}/blast-radius',
    { params: { path: { version, id } } },
  );

  if (error !== undefined || !data?.data)
    throw failed('custom event feed blast radius', response.status, error);

  return data.data;
}

export function useDeleteEventFeed(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, string, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, string, undefined>({
    mutationFn: async (id: string): Promise<void> => {
      const { error, response } = await client.DELETE('/api/v{version}/custom-data-sources/{id}', {
        params: { path: { version, id } },
      });

      if (error !== undefined)
        throw refusal('delete custom event feed', error, response.status);
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: eventFeedsKey(toValue(channelId)) });
    },
  });
}

export type TestEventFeed = { id: string; samplePayload: string };

// Fires the feed's `custom.<name>` event from a sample payload, so a pipeline can be wired and previewed.
export function useTestEventFeed(): UseMutationReturnType<void, Error, TestEventFeed, undefined> {
  return useMutation<void, Error, TestEventFeed, undefined>({
    mutationFn: async ({ id, samplePayload }: TestEventFeed): Promise<void> => {
      const { error, response } = await client.POST('/api/v{version}/custom-data-sources/{id}/test', {
        params: { path: { version, id } },
        body: { samplePayload },
      });

      if (error !== undefined)
        throw refusal('test custom event feed', error, response.status);
    },
  });
}

// One fetch of the feed's address: the body and every key path in it, so a field is picked and not typed blind.
export function useFetchEventFeed(): UseMutationReturnType<
  EventFeedFetch,
  Error,
  string,
  undefined
> {
  return useMutation<EventFeedFetch, Error, string, undefined>({
    mutationFn: async (id: string): Promise<EventFeedFetch> => {
      const { data, error, response } = await client.POST(
        '/api/v{version}/custom-data-sources/{id}/test-fetch',
        { params: { path: { version, id } } },
      );

      const fetched = data?.data;

      if (error !== undefined || fetched === undefined || fetched === null)
        throw refusal('fetch custom event feed', error, response.status);

      return fetched;
    },
  });
}
