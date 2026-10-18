// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { UseMutationReturnType, UseQueryReturnType } from '@tanstack/vue-query';
import { toValue, type MaybeRefOrGetter } from 'vue';

import { useOptimisticSave } from '@/composables/useOptimisticSave';
import type { KeyParts } from '@/lib/array';

import { channelQuery, failed, refusal } from './channelQuery';
import { client } from './client';
import type { components } from './schema';

// The tone the built-in commands answer in. `available` lists every tone the server accepts, so the
// picker needs no list of its own.
export type Personality = components['schemas']['ChannelPersonality'];

const version = '1';

// Marked stale by the "channel-settings" config change the server publishes after a save (configDomains).
export const personalityKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'settings',
  'personality',
];

async function readPersonality(channelId: string): Promise<Personality> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/settings/personality',
    { params: { path: { version, channelId } } },
  );

  const found = data?.data;

  if (error !== undefined || found === undefined || found === null)
    throw failed('personality settings', response.status, error);

  return found;
}

export function usePersonality(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<Personality, Error> {
  return channelQuery(channelId, personalityKey, readPersonality);
}

// The picked tone shows at once and rolls back when the bot refuses it. The bot answers with the tone it
// stored, which is the one the cache keeps.
export function useSetPersonality(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<Personality, Error, string, number> {
  return useOptimisticSave<Personality, string>({
    describe: (tone: string) => ({ key: 'setPersonality', params: { tone } }),
    queryKey: () => personalityKey(toValue(channelId)),
    mutationFn: async (tone: string): Promise<Personality> => {
      const { data, error, response } = await client.PUT(
        '/api/v{version}/channels/{channelId}/settings/personality',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: { personality: tone },
        },
      );

      const stored = data?.data;

      if (error !== undefined || stored === undefined || stored === null)
        throw refusal('save personality', error, response.status);

      return stored;
    },
    apply: (current: Personality | undefined, tone: string) => ({
      available: current?.available ?? [],
      personality: tone,
    }) as const satisfies Personality,
  });
}
