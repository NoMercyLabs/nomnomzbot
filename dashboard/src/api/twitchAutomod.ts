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

// Twitch's own AutoMod: eight categories, each held at a level from 0 (off) to 4 (strictest). Twitch keeps the
// levels, so every read is live and a save answers with what Twitch now holds.
export type TwitchAutomodSettings = components['schemas']['TwitchAutoModSettings'];

export const TWITCH_AUTOMOD_CATEGORIES = [
  'aggression',
  'bullying',
  'disability',
  'misogyny',
  'raceEthnicityOrReligion',
  'sexBasedTerms',
  'sexualitySexOrGender',
  'swearing',
] as const satisfies readonly (keyof TwitchAutomodSettings)[];

export type TwitchAutomodCategory = (typeof TWITCH_AUTOMOD_CATEGORIES)[number];
export type TwitchAutomodLevels = Record<TwitchAutomodCategory, number>;

export const TWITCH_AUTOMOD_LEVELS = [0, 1, 2, 3, 4] as const;

const version = '1';

export const twitchAutomodKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'twitch-automod',
];

async function fetchLevels(channelId: string): Promise<TwitchAutomodSettings> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/moderation/automod/twitch',
    { params: { path: { version, channelId } } },
  );

  if (error !== undefined || !data?.data)
    throw failed('twitch automod', response.status, error);

  return data.data;
}

export function useTwitchAutomod(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<TwitchAutomodSettings, Error> {
  return channelQuery(channelId, twitchAutomodKey, fetchLevels);
}

// Twitch takes the eight categories without an overall level, so a save sends all eight.
export function useSaveTwitchAutomod(channelId: MaybeRefOrGetter<string | null>) {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async (levels: TwitchAutomodLevels): Promise<TwitchAutomodSettings> => {
      const { data, error, response } = await client.PUT(
        '/api/v{version}/channels/{channelId}/moderation/automod/twitch',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: levels,
        },
      );

      if (error !== undefined || !data?.data)
        throw failed('save twitch automod', response.status, error);

      return data.data;
    },
    onSuccess: (saved: TwitchAutomodSettings): void => {
      const key = twitchAutomodKey(toValue(channelId));

      queries.setQueryData(key, saved);
    },
  });
}
