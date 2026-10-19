// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { UseMutationReturnType, UseQueryReturnType } from '@tanstack/vue-query';
import { toValue, type MaybeRefOrGetter } from 'vue';

import { channelOptions, channelQuery, failed } from './channelQuery';
import { client } from './client';
import type { components } from './schema';
import { useOptimisticSave } from '@/composables/useOptimisticSave';
import { chunk, type KeyParts } from '@/lib/array';

export type Reward = components['schemas']['RewardDetail'];
export type Redemption = components['schemas']['RedemptionListItem'];
export type RewardAvailability = components['schemas']['RewardAvailability'];
export type RewardChange = components['schemas']['UpdateRewardRequest'];

const version = '1';
const pageSize = 100;

export const rewardsKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'rewards',
];

export const availabilityKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'rewards',
  'availability',
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

async function readAvailability(channelId: string): Promise<RewardAvailability> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/rewards/availability',
    { params: { path: { version, channelId } } },
  );

  if (error !== undefined || !data?.data)
    throw failed('rewards', response.status, error);

  return data.data;
}

// Twitch gives channel points to Affiliates and Partners only; the page says so when the channel is neither.
export function useRewardAvailability(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<RewardAvailability, Error> {
  return channelQuery(channelId, availabilityKey, readAvailability);
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

export const rewardsOptions = (id: string) => channelOptions(rewardsKey, readRewards, id);

export const redemptionsOptions = (id: string) =>
  channelOptions(redemptionsKey, readRedemptions, id);

export type RewardEdit = { rewardId: string; change: RewardChange };

function withChange(rewards: Reward[] | undefined, { rewardId, change }: RewardEdit): Reward[] {
  return (rewards ?? []).map((reward) => {
    if (reward.id !== rewardId)
      return reward;

    return {
      ...reward,
      title: change.title ?? reward.title,
      cost: change.cost ?? reward.cost,
      isEnabled: change.isEnabled ?? reward.isEnabled,
      isPaused: change.isPaused ?? reward.isPaused,
      prompt: change.prompt ?? reward.prompt,
      response: change.response ?? reward.response,
      isUserInputRequired: change.isUserInputRequired ?? reward.isUserInputRequired,
      backgroundColor: change.backgroundColor ?? reward.backgroundColor,
      maxPerStream: change.maxPerStream ?? reward.maxPerStream,
      maxPerUserPerStream: change.maxPerUserPerStream ?? reward.maxPerUserPerStream,
      globalCooldownSeconds: change.globalCooldownSeconds ?? reward.globalCooldownSeconds,
      timerDurationSeconds: change.timerDurationSeconds ?? reward.timerDurationSeconds,
      pipelineId: change.pipelineId ?? reward.pipelineId,
      speaksViaTts: change.speaksViaTts ?? reward.speaksViaTts,
    } as const satisfies Reward;
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
export type RedemptionDecision = { ids: string[]; action: RedemptionAction };

// Complete marks the redemptions done; reject refunds the viewers' points (Helix status CANCELED).
const decidedStatus = {
  fulfill: 'fulfilled',
  refund: 'canceled',
} as const satisfies Record<RedemptionAction, string>;

// The bot takes one redemption per call, so a bulk decision goes out in short runs of parallel calls.
const callsAtOnce = 10;

async function decide(
  channelId: string,
  redemptionId: string,
  action: RedemptionAction,
): Promise<void> {
  const path = action === 'fulfill'
    ? '/api/v{version}/channels/{channelId}/rewards/redemptions/{redemptionId}/fulfill'
    : '/api/v{version}/channels/{channelId}/rewards/redemptions/{redemptionId}/refund';
  const { error, response } = await client.POST(path, {
    params: { path: {
      version,
      channelId,
      redemptionId,
    } },
  });

  if (error !== undefined)
    throw failed(`${action} redemption`, response.status, error);
}

export function withDecision(
  rows: Redemption[] | undefined,
  { ids, action }: RedemptionDecision,
): Redemption[] {
  return (rows ?? []).map((row) =>
    ids.includes(row.redemptionId)
      ? { ...row, status: decidedStatus[action] } as const satisfies Redemption
      : row);
}

// The rows leave the pending list at once. A refusal puts them back and reaches the error inbox; the queue
// is then read again, because some of a bulk decision may have gone through.
export function useRedemptionDecision(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<Redemption[], Error, RedemptionDecision, number> {
  return useOptimisticSave<Redemption[], RedemptionDecision>({
    describe: ({ ids, action }: RedemptionDecision) => ({
      key: action === 'fulfill'
        ? 'completeRedemptions'
        : 'rejectRedemptions',
      params: { count: String(ids.length) },
    }),
    queryKey: () => redemptionsKey(toValue(channelId)),
    mutationFn: async ({ ids, action }: RedemptionDecision): Promise<Redemption[]> => {
      const id = toValue(channelId) as string;

      for (const run of chunk(ids, callsAtOnce)) {
        await Promise.all(run.map((redemptionId) =>
          decide(id, redemptionId, action)));
      }

      return readRedemptions(id);
    },
    apply: withDecision,
  });
}
