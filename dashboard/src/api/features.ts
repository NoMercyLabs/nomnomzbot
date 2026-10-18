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

// A feature of the channel: its switch, what it needs from Twitch, and whether the channel's plan includes it.
export type ChannelFeature = components['schemas']['FeatureStatus'];

const version = '1';

export const featuresKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'features',
];

async function readFeatures(channelId: string): Promise<ChannelFeature[]> {
  const { data, error, response } = await client.GET('/api/v{version}/channels/{channelId}/features', {
    params: { path: { channelId, version } },
  });

  const rows = data?.data;

  if (error !== undefined || rows === undefined || rows === null)
    throw failed('channel features', response.status, error);

  return rows;
}

export function useChannelFeatures(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<ChannelFeature[], Error> {
  return channelQuery(channelId, featuresKey, readFeatures);
}

// The bot flips the switch (there is no "set"): the answer is the feature as it stands after the flip.
export function useToggleFeature(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<ChannelFeature, Error, string, undefined> {
  const queries = useQueryClient();

  return useMutation<ChannelFeature, Error, string, undefined>({
    mutationFn: async (featureKey: string): Promise<ChannelFeature> => {
      const { data, error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/features/{featureKey}/toggle',
        { params: { path: {
          channelId: toValue(channelId) as string,
          featureKey,
          version,
        } } },
      );

      const row = data?.data;

      if (error !== undefined)
        throw refusal('toggle feature', error, response.status);

      if (row === undefined || row === null)
        throw failed('toggle feature', response.status);

      return row;
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: featuresKey(toValue(channelId)) });
    },
  });
}
