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

export type EventResponse = components['schemas']['EventResponseListItem'];
export type EventResponseDetail = components['schemas']['EventResponse'];
export type EventResponsePreset = components['schemas']['EventResponsePreset'];
export type EventResponseChange = components['schemas']['UpdateEventResponse'];

const version = '1';
// One response per event type; the event catalog stays well under this.
const pageSize = 200;

export const eventResponsesKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'event-responses',
];

// Under eventResponsesKey, so the one live "event-responses" signal marks the list, every open response
// and the catalog stale together.
export const eventResponseKey = (channelId: string | null, eventType: string): KeyParts => [
  ...eventResponsesKey(channelId),
  eventType,
];

export const eventPresetsKey = (channelId: string | null): KeyParts => [
  ...eventResponsesKey(channelId),
  'catalog',
];

export function useEventResponses(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<EventResponse[], Error> {
  return channelQuery(
    channelId,
    eventResponsesKey,
    async (id: string): Promise<EventResponse[]> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/channels/{channelId}/event-responses',
        {
          params: { path: { version, channelId: id }, query: { PageSize: pageSize } },
        },
      );

      if (error !== undefined)
        throw failed('event responses', response.status);

      return [...(data?.data ?? [])];
    },
  );
}

// The full response of one event type: the list rows carry no message text.
export function useEventResponse(
  channelId: MaybeRefOrGetter<string | null>,
  eventType: MaybeRefOrGetter<string>,
): UseQueryReturnType<EventResponseDetail, Error> {
  return channelQuery(
    channelId,
    (id: string | null): KeyParts => eventResponseKey(id, toValue(eventType)),
    async (id: string): Promise<EventResponseDetail> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/channels/{channelId}/event-responses/{eventType}',
        { params: { path: {
          version,
          channelId: id,
          eventType: toValue(eventType),
        } } },
      );
      const found = data?.data;

      if (error !== undefined || found === undefined || found === null)
        throw failed('event response', response.status);

      return found;
    },
  );
}

// The ready-made default text and the variables each event type fills.
export function useEventPresets(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<EventResponsePreset[], Error> {
  return channelQuery(
    channelId,
    eventPresetsKey,
    async (id: string): Promise<EventResponsePreset[]> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/channels/{channelId}/event-responses/catalog',
        { params: { path: { version, channelId: id } } },
      );

      if (error !== undefined)
        throw failed('event responses', response.status);

      return [...(data?.data ?? [])];
    },
  );
}

type Change = { eventType: string; change: EventResponseChange };

// Only the changed fields travel in the PUT. A refusal keeps the bot's reason on the mutation error.
export function useUpdateEventResponse(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, Change, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, Change, undefined>({
    mutationFn: async ({ eventType, change }): Promise<void> => {
      const { error, response } = await client.PUT(
        '/api/v{version}/channels/{channelId}/event-responses/{eventType}',
        {
          params: { path: {
            version,
            channelId: toValue(channelId) as string,
            eventType,
          } },
          body: change,
        },
      );

      if (error !== undefined)
        throw refusal('save event response', error, response.status);
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: eventResponsesKey(toValue(channelId)) });
    },
  });
}

// Drops the channel's own text and makes the event follow the platform default again. It always sends
// confirm: the server refuses to unbind a pipeline, command or action unless the caller says it is meant,
// and every caller of this hook asks the person in a dialog first.
export function useResetEventResponse(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, string, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, string, undefined>({
    mutationFn: async (eventType: string): Promise<void> => {
      const { error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/event-responses/{eventType}/reset',
        {
          params: {
            path: {
              version,
              channelId: toValue(channelId) as string,
              eventType,
            },
            query: { confirm: true },
          },
        },
      );

      if (error !== undefined)
        throw refusal('reset event response', error, response.status);
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: eventResponsesKey(toValue(channelId)) });
    },
  });
}
