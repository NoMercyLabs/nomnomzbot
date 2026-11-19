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

// The pending unban requests: Twitch holds them, and the moderator's own token reads and resolves them.
export type UnbanRequest = components['schemas']['UnbanRequest'];

const version = '1';

export const unbanRequestsKey = (channelId: string | null): KeyParts => ['channels', channelId, 'unban-requests'];

async function readUnbanRequests(channelId: string): Promise<UnbanRequest[]> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/moderation/unban-requests',
    { params: { path: { version, channelId }, query: { status: 'pending' } } },
  );

  if (error !== undefined)
    throw failed('unban requests', response.status, error);

  return [...(data?.data ?? [])];
}

export function useUnbanRequests(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<UnbanRequest[], Error> {
  return channelQuery(channelId, unbanRequestsKey, readUnbanRequests);
}

// Approving lifts the ban, so the server takes it only with confirm set; a deny needs no confirm.
export function useResolveUnbanRequest(channelId: MaybeRefOrGetter<string | null>) {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async ({ id, approve }: { id: string; approve: boolean }): Promise<void> => {
      const body = approve
        ? { approve, confirm: true }
        : { approve };
      const { error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/moderation/unban-requests/{unbanRequestId}/resolve',
        {
          params: { path: {
            version,
            channelId: toValue(channelId) as string,
            unbanRequestId: id,
          } },
          body,
        },
      );

      if (error !== undefined)
        throw failed('resolve unban request', response.status, error);
    },
    onSuccess: () => queries.invalidateQueries({ queryKey: unbanRequestsKey(toValue(channelId)) }),
  });
}
