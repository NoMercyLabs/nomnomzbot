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
  useQuery,
  useQueryClient,
  type UseMutationReturnType,
  type UseQueryReturnType,
} from '@tanstack/vue-query';
import { computed, toValue, type MaybeRefOrGetter } from 'vue';

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

// What the media share overlay last reported. The bot has no read for it, so the realtime event fills it.
export type MediaSharePlayerState = {
  state: 'idle' | 'playing' | 'paused' | 'ended' | 'error';
  requestId: string | null;
  title: string | null;
  sharerName: string | null;
  thumbnailUrl: string | null;
  positionSeconds: number;
  durationSeconds: number;
  volume: number;
};

export type MediaPlaybackCommand = 'play' | 'pause' | 'stop' | 'skip' | 'volume';

export type MediaPlaybackRequest = { command: MediaPlaybackCommand; value?: number };

export const mediaSharePlayerKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'media-share-player',
];

export function useMediaSharePlayer(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<MediaSharePlayerState | null, Error> {
  const queries = useQueryClient();
  const key = computed(() => mediaSharePlayerKey(toValue(channelId)));

  return useQuery({
    queryKey: key,
    queryFn: () =>
      queries.getQueryData<MediaSharePlayerState>(key.value) ?? null,
    staleTime: Infinity,
  });
}

// One player command through the bot's media_playback action; the bot then drives the overlay.
export function useMediaSharePlayback(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, MediaPlaybackRequest, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, MediaPlaybackRequest, undefined>({
    mutationFn: async ({ command, value }): Promise<void> => {
      const { data, error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/actions/run',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: {
            actionType: 'media_playback',
            parameters: { command, value: value === undefined
              ? ''
              : String(value) },
          },
        },
      );

      if (error !== undefined)
        throw refusal('media share action', error, response.status);

      if (data?.data?.success === false)
        throw refusal('media share action', { detail: data.data.error }, 422);
    },
    onSuccess: async (_data, request): Promise<void> => {
      const key = mediaSharePlayerKey(toValue(channelId));

      if (request.command === 'volume' && request.value !== undefined) {
        const volume = request.value;

        queries.setQueryData<MediaSharePlayerState | null>(key, (player) =>
          player
            ? { ...player, volume }
            : player);
      }

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
