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

import { useOptimisticSave } from '@/composables/useOptimisticSave';
import type { KeyParts } from '@/lib/array';

import { channelQuery, failed, refusal } from './channelQuery';
import { client } from './client';
import { timersKey, type Timer as TimerRow } from './liveOps';
import type { components } from './schema';

// The list reads (timersKey, useTimers, useToggleTimer) live in liveOps; this file adds the one-timer
// read and the create, update and delete writes of the Triggers and Timers page.
export type Timer = components['schemas']['Timer'];
export type NewTimer = components['schemas']['CreateTimer'];
export type TimerChange = components['schemas']['UpdateTimer'];

const version = '1';
const pageSize = 100;

// Under timersKey, so one live "timers" signal marks the list and every open timer stale together.
export const timerKey = (channelId: string | null, timerId: string): KeyParts => [
  ...timersKey(channelId),
  timerId,
];

async function readTimer(channelId: string, timerId: string): Promise<Timer> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/timers/{id}',
    {
      params: { path: {
        version,
        channelId,
        id: timerId,
      } },
    },
  );

  const found = data?.data;

  if (error !== undefined || found === undefined || found === null)
    throw failed('timer', response.status, error);

  return found;
}

async function readRows(channelId: string): Promise<TimerRow[]> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/timers',
    {
      params: { path: { version, channelId }, query: { PageSize: pageSize } },
    },
  );

  if (error !== undefined)
    throw failed('timers', response.status, error);

  return [...(data?.data ?? [])];
}

// The full timer: the list rows carry no messages.
export function useTimer(
  channelId: MaybeRefOrGetter<string | null>,
  timerId: MaybeRefOrGetter<string>,
): UseQueryReturnType<Timer, Error> {
  return channelQuery(
    channelId,
    (id: string | null): KeyParts => timerKey(id, toValue(timerId)),
    (id: string): Promise<Timer> => readTimer(id, toValue(timerId)),
  );
}

// A new timer is not shown before the bot accepts it: the name can be taken and the quota can be full.
export function useCreateTimer(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, NewTimer, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, NewTimer, undefined>({
    mutationFn: async (timer: NewTimer): Promise<void> => {
      const { error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/timers',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: timer,
        },
      );

      if (error !== undefined)
        throw refusal('create timer', error, response.status);
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: timersKey(toValue(channelId)) });
    },
  });
}

// Only the changed fields travel in the PUT. A refusal keeps the bot's reason on the mutation error.
export function useUpdateTimer(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, { timerId: string; change: TimerChange }, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, { timerId: string; change: TimerChange }, undefined>({
    mutationFn: async ({ timerId, change }): Promise<void> => {
      const { error, response } = await client.PUT(
        '/api/v{version}/channels/{channelId}/timers/{id}',
        {
          params: { path: {
            version,
            channelId: toValue(channelId) as string,
            id: timerId,
          } },
          body: change,
        },
      );

      if (error !== undefined)
        throw refusal('save timer', error, response.status);
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: timersKey(toValue(channelId)) });
    },
  });
}

// The row leaves the list at once; a refused delete puts it back and reaches the error inbox.
export function useDeleteTimer(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<TimerRow[], Error, { timerId: string; name: string }, number> {
  return useOptimisticSave<TimerRow[], { timerId: string; name: string }>({
    describe: ({ name }) => ({ key: 'deleteTimer', params: { name } }),
    queryKey: () => timersKey(toValue(channelId)),
    mutationFn: async ({ timerId }): Promise<TimerRow[]> => {
      const id = toValue(channelId) as string;
      const { error, response } = await client.DELETE(
        '/api/v{version}/channels/{channelId}/timers/{id}',
        {
          params: { path: {
            version,
            channelId: id,
            id: timerId,
          } },
        },
      );

      if (error !== undefined)
        throw refusal('delete timer', error, response.status);

      return readRows(id);
    },
    apply: (current, { timerId }): TimerRow[] =>
      (current ?? []).filter((row) => row.id !== timerId),
  });
}
