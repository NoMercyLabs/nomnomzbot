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

// Whether an admin network block also bans its blocked actor in this channel. The bot keeps the answer on the
// channel and starts at no: the owner opts in.
export type NetworkBlockOptIn = components['schemas']['NetworkBlockOptIn'];

const version = '1';

export const networkBlockOptInKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'network-block-opt-in',
];

async function readOptIn(channelId: string): Promise<NetworkBlockOptIn> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/moderation/network-block-opt-in',
    { params: { path: { version, channelId } } },
  );

  const answer = data?.data;

  if (error !== undefined || answer === undefined || answer === null)
    throw failed('network block opt-in', response.status, error);

  return answer;
}

export function useNetworkBlockOptIn(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<NetworkBlockOptIn, Error> {
  return channelQuery(channelId, networkBlockOptInKey, readOptIn);
}

// Only the broadcaster may change it. The bot answers 204, so the refetch is the source of the saved value.
export function useSaveNetworkBlockOptIn(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, NetworkBlockOptIn, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, NetworkBlockOptIn, undefined>({
    mutationFn: async (answer: NetworkBlockOptIn): Promise<void> => {
      const { error, response } = await client.PUT(
        '/api/v{version}/channels/{channelId}/moderation/network-block-opt-in',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: answer,
        },
      );

      if (error !== undefined)
        throw refusal('save network block opt-in', error, response.status);
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: networkBlockOptInKey(toValue(channelId)) });
    },
  });
}
