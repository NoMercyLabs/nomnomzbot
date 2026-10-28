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

import { useMutation, useQueryClient, type UseQueryReturnType } from '@tanstack/vue-query';
import { toValue, type MaybeRefOrGetter } from 'vue';

import type { KeyParts } from '@/lib/array';

import { channelQuery, failed } from './channelQuery';
import { client } from './client';
import type { components } from './schema';

// The people banned in the channel: Twitch holds the list, so every read is live and an unban shows at once.
export type BannedUser = components['schemas']['BannedUser'];

const version = '1';

export const bansKey = (channelId: string | null): KeyParts => ['channels', channelId, 'bans'];

async function readBans(channelId: string): Promise<BannedUser[]> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/moderation/bans',
    { params: { path: { version, channelId } } },
  );

  if (error !== undefined)
    throw failed('bans', response.status, error);

  return [...(data?.data ?? [])];
}

export function useBans(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<BannedUser[], Error> {
  return channelQuery(channelId, bansKey, readBans);
}

// A ban is lifted with no confirm: banning the person again undoes it.
export function useLiftBanned(channelId: MaybeRefOrGetter<string | null>) {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async (ban: BannedUser): Promise<void> => {
      const { error, response } = await client.DELETE(
        '/api/v{version}/channels/{channelId}/moderation/bans/{userId}',
        { params: { path: {
          version,
          channelId: toValue(channelId) as string,
          userId: ban.userId,
        } } },
      );

      if (error !== undefined)
        throw failed('unban from list', response.status, error);
    },
    onSuccess: () => queries.invalidateQueries({ queryKey: bansKey(toValue(channelId)) }),
  });
}
