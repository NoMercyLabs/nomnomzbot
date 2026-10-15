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

import { channelQuery, failed } from './channelQuery';
import { client } from './client';
import type { components } from './schema';
import { useOptimisticSave } from '@/composables/useOptimisticSave';
import type { KeyParts } from '@/lib/array';

export type Reward = components['schemas']['RewardDetail'];
export type Redemption = components['schemas']['RedemptionListItem'];
export type RewardChange = components['schemas']['UpdateRewardRequest'];

const version = '1';
const pageSize = 100;

export const rewardsKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'rewards',
];

// The redemption queue sits under the rewards key, so one refresh of the rewards marks both stale.
export const redemptionsKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'rewards',
  'redemptions',
];

async function readRewards(channelId: string): Promise<Reward[]> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/rewards',
    {
      params: { path: { version, channelId }, query: { PageSize: pageSize } },
    },
  );

  if (error !== undefined)
    throw failed('rewards', response.status, error);

  return [...(data?.data ?? [])];
}

async function readRedemptions(channelId: string): Promise<Redemption[]> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/rewards/redemptions',
    {
      params: { path: { version, channelId }, query: { PageSize: pageSize } },
    },
  );

  if (error !== undefined)
    throw failed('redemptions', response.status, error);

  return [...(data?.data ?? [])];
}

export function useRewards(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<Reward[], Error> {
  return channelQuery(channelId, rewardsKey, readRewards);
}

export function useRedemptions(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<Redemption[], Error> {
  return channelQuery(channelId, redemptionsKey, readRedemptions);
}

export type RewardEdit = { rewardId: string; change: RewardChange };

function withChange(rewards: Reward[] | undefined, { rewardId, change }: RewardEdit): Reward[] {
  return (rewards ?? []).map((reward: Reward): Reward => {
    if (reward.id !== rewardId)
      return reward;

    return {
      ...reward,
      title: change.title ?? reward.title,
      cost: change.cost ?? reward.cost,
      isEnabled: change.isEnabled ?? reward.isEnabled,
      isPaused: change.isPaused ?? reward.isPaused,
    };
  });
}

// Only the changed fields travel in the PATCH; the list shows them at once and then holds what the bot returns.
export function useUpdateReward(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<Reward[], Error, RewardEdit, number> {
  return useOptimisticSave<Reward[], RewardEdit>({
    describe: () => ({ key: 'updateReward' }),
    queryKey: () => rewardsKey(toValue(channelId)),
    mutationFn: async ({ rewardId, change }: RewardEdit): Promise<Reward[]> => {
      const id = toValue(channelId) as string;
      const { error, response } = await client.PATCH(
        '/api/v{version}/channels/{channelId}/rewards/{rewardId}',
        {
          params: { path: {
            version,
            channelId: id,
            rewardId,
          } },
          body: change,
        },
      );

      if (error !== undefined)
        throw failed('update reward', response.status, error);

      return readRewards(id);
    },
    apply: withChange,
  });
}

export type RedemptionAction = 'fulfill' | 'refund';

// Fulfil marks the redemption done; refund gives the viewer their points back. Twitch decides the new status,
// so the queue is read again instead of guessed.
export function useRedemptionAction(
  channelId: MaybeRefOrGetter<string | null>,
  action: RedemptionAction,
) {
  const queries = useQueryClient();

  return useMutation<void, Error, { redemptionId: string }>({
    mutationFn: async ({ redemptionId }: { redemptionId: string }): Promise<void> => {
      const id = toValue(channelId) as string;
      const path = action === 'fulfill'
        ? '/api/v{version}/channels/{channelId}/rewards/redemptions/{redemptionId}/fulfill'
        : '/api/v{version}/channels/{channelId}/rewards/redemptions/{redemptionId}/refund';
      const { error, response } = await client.POST(path, {
        params: { path: {
          version,
          channelId: id,
          redemptionId,
        } },
      });

      if (error !== undefined)
        throw failed(`${action} redemption`, response.status, error);
    },
    onSettled: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: redemptionsKey(toValue(channelId)) });
    },
  });
}
