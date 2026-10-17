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

// The channel basics of the Settings page: command prefix, bot line marker, language, timezone, auto-join.
// The display name and the subscription tier are not here: the bot reads the name from Twitch, and the
// tier is set by the plan, so neither has a value to show or a reason to edit.
export type ChannelBasics = components['schemas']['ChannelBasics'];
export type BasicsChange = components['schemas']['UpdateChannelSettings'];

const version = '1';

export const basicsKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'settings',
  'basics',
];

async function readBasics(channelId: string): Promise<ChannelBasics> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/settings/basics',
    { params: { path: { version, channelId } } },
  );

  const found = data?.data;

  if (error !== undefined || found === undefined || found === null)
    throw failed('basics settings', response.status, error);

  return found;
}

export function useBasics(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<ChannelBasics, Error> {
  return channelQuery(channelId, basicsKey, readBasics);
}

// The bot echoes what it saved, so the page shows the stored values and not the typed ones. A refusal keeps
// the bot's reason on the mutation error (a prefix with a space, a locale it does not know).
export function useUpdateBasics(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<ChannelBasics, Error, BasicsChange, undefined> {
  const queries = useQueryClient();

  return useMutation<ChannelBasics, Error, BasicsChange, undefined>({
    mutationFn: async (change: BasicsChange): Promise<ChannelBasics> => {
      const { data, error, response } = await client.PUT(
        '/api/v{version}/channels/{channelId}/settings/basics',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: change,
        },
      );

      const saved = data?.data;

      if (error !== undefined || saved === undefined || saved === null)
        throw refusal('save basics', error, response.status);

      return saved;
    },
    onSuccess: (saved: ChannelBasics): void => {
      const key = basicsKey(toValue(channelId));

      queries.setQueryData(key, saved);
    },
  });
}
