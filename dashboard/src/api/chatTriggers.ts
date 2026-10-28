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

// The chat triggers of the Triggers and Timers page: keyword auto-replies on ordinary chat lines.
// The bot publishes no ConfigChanged domain for chat triggers, so every write refetches the list itself.
export type ChatTrigger = components['schemas']['ChatTrigger'];
export type NewChatTrigger = components['schemas']['CreateChatTriggerRequest'];
export type ChatTriggerChange = components['schemas']['UpdateChatTriggerRequest'];

const version = '1';

export const chatTriggersKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'chat-triggers',
];

async function readTriggers(channelId: string): Promise<ChatTrigger[]> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/chat-triggers',
    { params: { path: { version, channelId } } },
  );

  if (error !== undefined)
    throw failed('chat triggers', response.status, error);

  return [...(data?.data ?? [])];
}

export function useChatTriggers(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<ChatTrigger[], Error> {
  return channelQuery(channelId, chatTriggersKey, readTriggers);
}

export function useCreateChatTrigger(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, NewChatTrigger, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, NewChatTrigger, undefined>({
    mutationFn: async (trigger: NewChatTrigger): Promise<void> => {
      const { error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/chat-triggers',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: trigger,
        },
      );

      if (error !== undefined)
        throw refusal('create chat trigger', error, response.status);
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: chatTriggersKey(toValue(channelId)) });
    },
  });
}

// Only the changed fields travel in the PATCH. A refusal keeps the bot's reason on the mutation error.
export function useUpdateChatTrigger(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, { triggerId: string; change: ChatTriggerChange }, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, { triggerId: string; change: ChatTriggerChange }, undefined>({
    mutationFn: async ({ triggerId, change }): Promise<void> => {
      const { error, response } = await client.PATCH(
        '/api/v{version}/channels/{channelId}/chat-triggers/{triggerId}',
        {
          params: { path: {
            version,
            channelId: toValue(channelId) as string,
            triggerId,
          } },
          body: change,
        },
      );

      if (error !== undefined)
        throw refusal('save chat trigger', error, response.status);
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: chatTriggersKey(toValue(channelId)) });
    },
  });
}

export function useDeleteChatTrigger(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, string, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, string, undefined>({
    mutationFn: async (triggerId: string): Promise<void> => {
      const { error, response } = await client.DELETE(
        '/api/v{version}/channels/{channelId}/chat-triggers/{triggerId}',
        {
          params: { path: {
            version,
            channelId: toValue(channelId) as string,
            triggerId,
          } },
        },
      );

      if (error !== undefined)
        throw refusal('delete chat trigger', error, response.status);
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: chatTriggersKey(toValue(channelId)) });
    },
  });
}
