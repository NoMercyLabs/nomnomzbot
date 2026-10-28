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

// Whether a moderator's mass ban across their channels also reaches this channel. The bot keeps the answer on the
// channel; the owner can turn it off.
export type MassBanOptIn = components['schemas']['MassBanOptIn'];

const version = '1';

export const massBanOptInKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'mass-ban-opt-in',
];

async function readOptIn(channelId: string): Promise<MassBanOptIn> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/moderation/mass-ban-opt-in',
    { params: { path: { version, channelId } } },
  );

  const answer = data?.data;

  if (error !== undefined || answer === undefined || answer === null)
    throw failed('mass ban opt-in', response.status, error);

  return answer;
}

export function useMassBanOptIn(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<MassBanOptIn, Error> {
  return channelQuery(channelId, massBanOptInKey, readOptIn);
}

// Only the broadcaster may change it. The bot answers 204, so the refetch is the source of the saved value.
export function useSaveMassBanOptIn(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, MassBanOptIn, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, MassBanOptIn, undefined>({
    mutationFn: async (answer: MassBanOptIn): Promise<void> => {
      const { error, response } = await client.PUT(
        '/api/v{version}/channels/{channelId}/moderation/mass-ban-opt-in',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: answer,
        },
      );

      if (error !== undefined)
        throw refusal('save mass ban opt-in', error, response.status);
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: massBanOptInKey(toValue(channelId)) });
    },
  });
}
