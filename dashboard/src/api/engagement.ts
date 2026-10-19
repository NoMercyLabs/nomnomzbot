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

// The greeting and streak moments of the channel. The path has no channel id: the bot reads the
// channel from the signed-in session, so the id only keeps one cache entry per channel.
export type EngagementConfig = {
  firstTimeChatterEnabled: boolean;
  returningChatterEnabled: boolean;
  watchStreakEnabled: boolean;
  streakMilestones: number[];
  greetCooldownSeconds: number;
};

// What the bot uses for a channel that never saved a config (EngagementService.Defaults: every moment off,
// no milestones so a streak counts on every stream, a 5 second pace).
export const engagementDefaults: EngagementConfig = {
  firstTimeChatterEnabled: false,
  returningChatterEnabled: false,
  watchStreakEnabled: false,
  streakMilestones: [],
  greetCooldownSeconds: 5,
};

export type EngagementChange = Omit<
  components['schemas']['UpdateEngagementConfigRequest'],
  'streakMilestones' | 'greetCooldownSeconds'
> & {
  streakMilestones: number[];
  greetCooldownSeconds: number;
};

export const engagementConfigKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'engagement-config',
];

async function readConfig(): Promise<EngagementConfig> {
  const { data, error, response } = await client.GET('/api/v{version}/engagement/config', {
    params: { path: { version: '1' } },
  });

  const row = data?.data;

  if (error !== undefined || row === undefined || row === null)
    throw failed('engagement config', response.status, error);

  return {
    firstTimeChatterEnabled: row.firstTimeChatterEnabled,
    returningChatterEnabled: row.returningChatterEnabled,
    watchStreakEnabled: row.watchStreakEnabled,
    streakMilestones: row.streakMilestones.map(Number),
    greetCooldownSeconds: Number(row.greetCooldownSeconds),
  };
}

export function useEngagementConfig(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<EngagementConfig, Error> {
  return channelQuery(channelId, engagementConfigKey, readConfig);
}

// The bot accepts or refuses the whole config; a refusal keeps its reason on the mutation error.
export function useSaveEngagementConfig(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, EngagementChange, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, EngagementChange, undefined>({
    mutationFn: async (change: EngagementChange): Promise<void> => {
      const { error, response } = await client.PUT('/api/v{version}/engagement/config', {
        params: { path: { version: '1' } },
        body: change,
      });

      if (error !== undefined)
        throw refusal('save engagement config', error, response.status);
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: engagementConfigKey(toValue(channelId)) });
    },
  });
}
