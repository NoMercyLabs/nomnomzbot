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
  type UseMutationReturnType,
  type UseQueryReturnType,
} from '@tanstack/vue-query';
import { toValue, type MaybeRefOrGetter } from 'vue';

import type { KeyParts } from '@/lib/array';

import { channelQuery, failed, refusal } from './channelQuery';
import { client } from './client';
import type { components } from './schema';

export type MediaShareRequest = components['schemas']['MediaShareRequest'];
export type MediaShareConfig = components['schemas']['MediaShareConfig'];
export type MediaShareConfigChange = components['schemas']['UpdateMediaShareConfigRequest'];
export type MediaShareAction = 'approve' | 'reject' | 'skip';

const version = '1';
const pageSize = 100;

// The endpoints take the channel from the signed-in session, so the channel id only keys the cache.
export const mediaShareQueueKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'media-share-queue',
];
export const mediaShareConfigKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'media-share-config',
];

async function readQueue(): Promise<MediaShareRequest[]> {
  const { data, error, response } = await client.GET('/api/v{version}/media-share/queue', {
    params: { path: { version }, query: { PageSize: pageSize } },
  });

  if (error !== undefined)
    throw failed('media share', response.status, error);

  return [...(data?.data ?? [])];
}

async function readConfig(): Promise<MediaShareConfig | null> {
  const { data, error, response } = await client.GET('/api/v{version}/media-share/config', {
    params: { path: { version } },
  });

  if (error !== undefined)
    throw failed('media share config', response.status, error);

  return data?.data ?? null;
}

export function useMediaShareQueue(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<MediaShareRequest[], Error> {
  return channelQuery(channelId, mediaShareQueueKey, readQueue);
}

export function useMediaShareConfig(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<MediaShareConfig | null, Error> {
  return channelQuery(channelId, mediaShareConfigKey, readConfig);
}

// Approve, reject and skip share one shape: the request id. A refused one keeps the bot's reason on the error.
export function useDecideMediaShare(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, { id: string; action: MediaShareAction }, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, { id: string; action: MediaShareAction }, undefined>({
    mutationFn: async ({ id, action }): Promise<void> => {
      const path = `/api/v{version}/media-share/{id}/${action}` as const;
      const { error, response } = await client.POST(path, {
        params: { path: { version, id } },
      });

      if (error !== undefined)
        throw refusal('media share action', error, response.status);
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: mediaShareQueueKey(toValue(channelId)) });
    },
  });
}

// The 1-based play position among the approved items.
export function useReorderMediaShare(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, { id: string; position: number }, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, { id: string; position: number }, undefined>({
    mutationFn: async ({ id, position }): Promise<void> => {
      const { error, response } = await client.POST('/api/v{version}/media-share/{id}/reorder', {
        params: { path: { version, id } },
        body: { position },
      });

      if (error !== undefined)
        throw refusal('media share action', error, response.status);
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: mediaShareQueueKey(toValue(channelId)) });
    },
  });
}

// The PUT carries the whole config: the bot has no partial update for it.
export function useSaveMediaShareConfig(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, MediaShareConfigChange, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, MediaShareConfigChange, undefined>({
    mutationFn: async (config: MediaShareConfigChange): Promise<void> => {
      const { error, response } = await client.PUT('/api/v{version}/media-share/config', {
        params: { path: { version } },
        body: config,
      });

      if (error !== undefined)
        throw refusal('save media share config', error, response.status);
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: mediaShareConfigKey(toValue(channelId)) });
    },
  });
}
