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

import type { SpamPolicy, SpamSettings } from './adminSpamDefaults';
import { channelQuery, failed, refusal } from './channelQuery';
import { client } from './client';

// A channel's spam-defense policy: the values, the catalog that bounds and groups every knob, and the guarantees
// that have no switch. The bot publishes no event when the policy is saved, so this page refetches after its own
// save; a second open tab shows the old values until it reloads.
export const spamDefensePolicyKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'moderation',
  'spam-defense',
];

async function readPolicy(channelId: string): Promise<SpamPolicy> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/spam-defense/policy',
    { params: { path: { version: '1', channelId } } },
  );

  const found = data?.data;

  if (error !== undefined || found === undefined || found === null)
    throw failed('spam defense policy', response.status, error);

  return found;
}

export function useSpamDefensePolicy(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<SpamPolicy, Error> {
  return channelQuery(channelId, spamDefensePolicyKey, readPolicy);
}

// A plain mutation: the whole record goes up, and the bot's refusal message comes back as the error.
export function useSaveSpamDefensePolicy(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, SpamSettings, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, SpamSettings, undefined>({
    mutationFn: async (body: SpamSettings): Promise<void> => {
      const { error, response } = await client.PUT(
        '/api/v{version}/channels/{channelId}/spam-defense/policy',
        {
          params: { path: { version: '1', channelId: toValue(channelId) as string } },
          body,
        },
      );

      if (error !== undefined)
        throw refusal('save spam defense policy', error, response.status);
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: spamDefensePolicyKey(toValue(channelId)) });
    },
  });
}
