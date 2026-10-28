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
  useQuery,
  useQueryClient,
  type UseQueryReturnType,
} from '@tanstack/vue-query';
import { computed, toValue, type MaybeRefOrGetter } from 'vue';

import { client } from './client';
import type { KeyParts } from '@/lib/array';

import { failed } from './errors';

export const shieldKey = (channelId: string | null): KeyParts => ['channels', channelId, 'shield'];

// The status reads live from the Twitch moderation API; the document types the answer as an open object.
async function fetchShield(channelId: string): Promise<boolean> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/moderation/shield',
    { params: { path: { version: '1', channelId } } },
  );

  // A failed read is not "shield off": the caller must see the error, never a guessed answer.
  if (error !== undefined)
    throw failed('shield', response.status, error);

  const answer = data?.data as { enabled?: boolean } | null | undefined;

  return answer?.enabled === true;
}

export function useShieldMode(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<boolean, Error> {
  return useQuery({
    queryKey: computed(() => shieldKey(toValue(channelId))),
    queryFn: () => fetchShield(toValue(channelId) as string),
    enabled: computed(() => toValue(channelId) !== null),
  });
}

export function useSetShieldMode(
  channelId: MaybeRefOrGetter<string | null>,
) {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async (enabled: boolean): Promise<void> => {
      const { error, response } = await client.PATCH(
        '/api/v{version}/channels/{channelId}/moderation/shield',
        {
          params: { path: { version: '1', channelId: toValue(channelId) as string } },
          body: { enabled },
        },
      );

      if (!response.ok)
        throw failed('shield', response.status, error);
    },
    onSuccess: () => queries.invalidateQueries({ queryKey: shieldKey(toValue(channelId)) }),
  });
}
