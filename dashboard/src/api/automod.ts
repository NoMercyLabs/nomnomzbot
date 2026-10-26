// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { useMutation, useQueryClient, type UseQueryReturnType } from '@tanstack/vue-query';
import { toValue, type MaybeRefOrGetter } from 'vue';

import type { KeyParts } from '@/lib/array';

import { channelQuery, failed } from './channelQuery';
import { client } from './client';
import type { components } from './schema';

// The channel's four built-in AutoMod filters: links, capital letters, banned phrases and emote spam. The save
// sends the whole record, so the heat fields the page does not edit travel back unchanged.
export type AutomodConfig = components['schemas']['AutomodConfig'];

const version = '1';

export const automodKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'automod',
];

async function fetchAutomod(channelId: string): Promise<AutomodConfig> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/moderation/automod',
    { params: { path: { version, channelId } } },
  );

  if (error !== undefined || !data?.data)
    throw failed('automod', response.status, error);

  return data.data;
}

export function useAutomod(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<AutomodConfig, Error> {
  return channelQuery(channelId, automodKey, fetchAutomod);
}

export function useSaveAutomod(channelId: MaybeRefOrGetter<string | null>) {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async (config: AutomodConfig): Promise<AutomodConfig> => {
      const { data, error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/moderation/automod',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: config,
        },
      );

      if (error !== undefined || !data?.data)
        throw failed('save automod', response.status, error);

      return data.data;
    },
    onSuccess: (saved: AutomodConfig): void => {
      const key = automodKey(toValue(channelId));

      queries.setQueryData(key, saved);
    },
  });
}
