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
  type UseQueryReturnType,
} from '@tanstack/vue-query';
import { computed, toValue, type MaybeRefOrGetter } from 'vue';

import type { KeyParts } from '@/lib/array';

import { failed } from './channelQuery';
import { client } from './client';
import { obsStateKey } from './obs';
import type { components } from './schema';

export type ObsStats = components['schemas']['ObsStats'];

export type ObsOutput = 'streaming' | 'recording' | 'replay-buffer' | 'virtual-cam';
export type ObsOutputAction = 'start' | 'stop' | 'pause' | 'resume' | 'split';

export type ObsOutputInput = { output: ObsOutput; action: ObsOutputAction };

const version = '1';

// The server enums ObsToggle (start, stop) and RecordAction (start, stop, toggle, pause, resume, split) travel as numbers.
const actionNumbers = {
  start: 0,
  stop: 1,
  pause: 3,
  resume: 4,
  split: 5,
} as const satisfies Record<ObsOutputAction, number>;

export const obsStatsKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'obs',
  'stats',
];

export const obsVirtualCamKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'obs',
  'virtual-cam',
];

// CPU, memory, FPS and frame counters. `active` is false while the page is hidden, so a background tab never polls OBS.
export function useObsStats(
  channelId: MaybeRefOrGetter<string | null>,
  connected: MaybeRefOrGetter<boolean>,
  active: MaybeRefOrGetter<boolean>,
  everyMs: number,
): UseQueryReturnType<ObsStats | null, Error> {
  return useQuery({
    queryKey: computed(() => obsStatsKey(toValue(channelId))),
    queryFn: async (): Promise<ObsStats | null> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/channels/{channelId}/obs/stats',
        { params: { path: { version, channelId: toValue(channelId) as string } } },
      );

      if (error !== undefined)
        throw failed('obs stats', response.status, error);

      return data?.data ?? null;
    },
    enabled: computed(() => toValue(channelId) !== null && toValue(connected)),
    refetchInterval: computed(() =>
      toValue(active)
        ? everyMs
        : false),
    retry: false,
  });
}

// The state read has no virtual camera field, so it has its own read.
export function useObsVirtualCam(
  channelId: MaybeRefOrGetter<string | null>,
  connected: MaybeRefOrGetter<boolean>,
): UseQueryReturnType<boolean, Error> {
  return useQuery({
    queryKey: computed(() => obsVirtualCamKey(toValue(channelId))),
    queryFn: async (): Promise<boolean> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/channels/{channelId}/obs/virtual-cam/status',
        { params: { path: { version, channelId: toValue(channelId) as string } } },
      );

      if (error !== undefined)
        throw failed('obs virtual cam', response.status, error);

      return data?.data?.outputActive === true;
    },
    enabled: computed(() => toValue(channelId) !== null && toValue(connected)),
    retry: false,
  });
}

type OutputPath = { version: string; channelId: string };

// Recording, streaming, replay buffer and virtual camera each have their own route; the body is the same shape.
function postOutput(output: ObsOutput, action: number, path: OutputPath) {
  const body = { action };

  if (output === 'recording')
    return client.POST('/api/v{version}/channels/{channelId}/obs/recording', {
      params: { path },
      body,
    });

  if (output === 'streaming')
    return client.POST('/api/v{version}/channels/{channelId}/obs/streaming', {
      params: { path },
      body,
    });

  if (output === 'replay-buffer')
    return client.POST('/api/v{version}/channels/{channelId}/obs/replay-buffer', {
      params: { path },
      body,
    });

  return client.POST('/api/v{version}/channels/{channelId}/obs/virtual-cam', {
    params: { path },
    body,
  });
}

// One call per output button; the live reads refetch afterwards so the row shows the new state.
export function useObsOutputAction(
  channelId: MaybeRefOrGetter<string | null>,
) {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async ({ output, action }: ObsOutputInput): Promise<void> => {
      const path = { version, channelId: toValue(channelId) as string };
      const { error, response } = await postOutput(output, actionNumbers[action], path);

      if (error !== undefined)
        throw failed('obs output', response.status, error);
    },
    onSuccess: async (): Promise<void> => {
      const id = toValue(channelId);

      await queries.invalidateQueries({ queryKey: obsStateKey(id) });
      await queries.invalidateQueries({ queryKey: obsVirtualCamKey(id) });
    },
  });
}

// Saves the last replay-buffer clip to disk.
export function useObsSaveReplay(
  channelId: MaybeRefOrGetter<string | null>,
) {
  return useMutation({
    mutationFn: async (): Promise<void> => {
      const { error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/obs/replay-buffer/save',
        { params: { path: { version, channelId: toValue(channelId) as string } } },
      );

      if (error !== undefined)
        throw failed('obs output', response.status, error);
    },
  });
}
