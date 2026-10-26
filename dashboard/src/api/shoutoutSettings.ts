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

// The channel's shoutout settings: whether a known streamer is shouted out on their first chat of a stream, and
// the message the bot announces. An empty template means the built-in default message.
export type AutoShoutout = components['schemas']['AutoShoutout'];
export type ShoutoutTemplate = components['schemas']['ShoutoutTemplate'];

const version = '1';

export const autoShoutoutKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'auto-shoutout',
];

export const shoutoutTemplateKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'shoutout-template',
];

async function readAutoShoutout(channelId: string): Promise<AutoShoutout> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/moderation/auto-shoutout',
    { params: { path: { version, channelId } } },
  );

  const answer = data?.data;

  if (error !== undefined || answer === undefined || answer === null)
    throw failed('auto shoutout', response.status, error);

  return answer;
}

async function readTemplate(channelId: string): Promise<ShoutoutTemplate> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/moderation/shoutout-template',
    { params: { path: { version, channelId } } },
  );

  const answer = data?.data;

  if (error !== undefined || answer === undefined || answer === null)
    throw failed('shoutout template', response.status, error);

  return answer;
}

export function useAutoShoutout(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<AutoShoutout, Error> {
  return channelQuery(channelId, autoShoutoutKey, readAutoShoutout);
}

export function useShoutoutTemplate(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<ShoutoutTemplate, Error> {
  return channelQuery(channelId, shoutoutTemplateKey, readTemplate);
}

// The bot answers 204, so the refetch is the source of the saved value.
export function useSaveAutoShoutout(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, AutoShoutout, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, AutoShoutout, undefined>({
    mutationFn: async (answer: AutoShoutout): Promise<void> => {
      const { error, response } = await client.PUT(
        '/api/v{version}/channels/{channelId}/moderation/auto-shoutout',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: answer,
        },
      );

      if (error !== undefined)
        throw refusal('save auto shoutout', error, response.status);
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: autoShoutoutKey(toValue(channelId)) });
    },
  });
}

// An empty template clears the custom message, so the default one is used again.
export function useSaveShoutoutTemplate(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, ShoutoutTemplate, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, ShoutoutTemplate, undefined>({
    mutationFn: async (answer: ShoutoutTemplate): Promise<void> => {
      const { error, response } = await client.PUT(
        '/api/v{version}/channels/{channelId}/moderation/shoutout-template',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: answer,
        },
      );

      if (error !== undefined)
        throw refusal('save shoutout template', error, response.status);
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: shoutoutTemplateKey(toValue(channelId)) });
    },
  });
}
