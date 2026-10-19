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

import { channelQuery, failed } from './channelQuery';
import { client } from './client';
import type { components } from './schema';

export type Schedule = components['schemas']['TwitchSchedule'];
export type ScheduleSegment = components['schemas']['TwitchScheduleSegment'];
export type ScheduleSettings = components['schemas']['UpdateScheduleSettings'];
export type NewSegment = components['schemas']['CreateScheduleSegmentRequest'];
export type SegmentChange = components['schemas']['UpdateScheduleSegmentRequest'];
type RaidRulesWire = components['schemas']['RaidScoringRules'];

// The server fills every key a stored blob left out (RaidScoringDefaults.Resolve) before it answers,
// so a read always carries the full set even though the contract marks defaulted keys optional.
export type RaidRules = Required<Omit<RaidRulesWire, 'recency' | 'balance'>> & {
  recency: Required<components['schemas']['RaidRecencyRules']>;
  balance: Required<components['schemas']['RaidBalanceWeights']>;
};

function resolvedRules(rules: RaidRulesWire | null | undefined): RaidRules | null {
  return (rules ?? null) as RaidRules | null;
}

export type Commercial = components['schemas']['TwitchCommercial'];

export type SegmentEdit = {
  segmentId: string;
  change: SegmentChange;
};

const version = '1';

export const scheduleKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'live-ops-schedule',
];

export const raidRulesKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'live-ops-raid-rules',
];

async function readSchedule(channelId: string): Promise<Schedule | null> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/live-ops/schedule',
    {
      params: { path: { version, channelId } },
    },
  );

  if (error !== undefined)
    throw failed('schedule', response.status, error);

  return data?.data ?? null;
}

// The channel's Twitch schedule: its segments and the vacation window, when one is set.
export function useSchedule(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<Schedule | null, Error> {
  return channelQuery(channelId, scheduleKey, readSchedule);
}

// Runs a change on the schedule, then reads the schedule again so the page shows what Twitch holds.
function useScheduleMutation<TVariables>(
  channelId: MaybeRefOrGetter<string | null>,
  send: (id: string, variables: TVariables) => Promise<void>,
): UseMutationReturnType<void, Error, TVariables, undefined> {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: (variables: TVariables): Promise<void> =>
      send(toValue(channelId) as string, variables),
    onSuccess: () =>
      queries.invalidateQueries({ queryKey: scheduleKey(toValue(channelId)) }),
  });
}

// Turns the vacation on or off and moves its window.
export function useSaveScheduleSettings(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, ScheduleSettings, undefined> {
  return useScheduleMutation(channelId, async (id: string, body: ScheduleSettings) => {
    const { error, response } = await client.PUT(
      '/api/v{version}/channels/{channelId}/live-ops/schedule/settings',
      {
        params: { path: { version, channelId: id } },
        body,
      },
    );

    if (error !== undefined)
      throw failed('schedule-settings', response.status, error);
  });
}

// Adds a segment, one time or weekly.
export function useCreateSegment(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, NewSegment, undefined> {
  return useScheduleMutation(channelId, async (id: string, body: NewSegment) => {
    const { error, response } = await client.POST(
      '/api/v{version}/channels/{channelId}/live-ops/schedule/segment',
      {
        params: { path: { version, channelId: id } },
        body,
      },
    );

    if (error !== undefined)
      throw failed('schedule-segment', response.status, error);
  });
}

// Changes one segment: its start, category, title, length, or whether it is canceled.
export function useUpdateSegment(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, SegmentEdit, undefined> {
  return useScheduleMutation(channelId, async (id: string, edit: SegmentEdit) => {
    const { error, response } = await client.PATCH(
      '/api/v{version}/channels/{channelId}/live-ops/schedule/segment/{segmentId}',
      {
        params: { path: {
          version,
          channelId: id,
          segmentId: edit.segmentId,
        } },
        body: edit.change,
      },
    );

    if (error !== undefined)
      throw failed('schedule-segment', response.status, error);
  });
}

async function readRaidRules(channelId: string): Promise<RaidRules | null> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/live-ops/raids/scoring-rules',
    {
      params: { path: { version, channelId } },
    },
  );

  if (error !== undefined)
    throw failed('raid-rules', response.status, error);

  return resolvedRules(data?.data);
}

// This channel's own raid target rules. A channel that never saved any gets the neutral set.
export function useRaidRules(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<RaidRules | null, Error> {
  return channelQuery(channelId, raidRulesKey, readRaidRules);
}

// Replaces the raid rules. The server refuses rules that break a limit and keeps the old ones.
export function useSaveRaidRules(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<RaidRules | null, Error, RaidRules, undefined> {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async (rules: RaidRules): Promise<RaidRules | null> => {
      const id = toValue(channelId) as string;
      const { data, error, response } = await client.PUT(
        '/api/v{version}/channels/{channelId}/live-ops/raids/scoring-rules',
        {
          params: { path: { version, channelId: id } },
          body: rules,
        },
      );

      if (error !== undefined)
        throw failed('raid-rules-save', response.status, error);

      return resolvedRules(data?.data);
    },
    onSuccess: (saved: RaidRules | null) => {
      const key = raidRulesKey(toValue(channelId));

      queries.setQueryData(key, saved);
    },
  });
}

// Runs an ad break on Twitch. The variable is its length in seconds.
export function useStartCommercial(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<Commercial | null, Error, number, undefined> {
  return useMutation({
    mutationFn: async (lengthSeconds: number): Promise<Commercial | null> => {
      const { data, error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/live-ops/ads/commercial',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: { lengthSeconds },
        },
      );

      if (error !== undefined)
        throw failed('commercial', response.status, error);

      return data?.data ?? null;
    },
  });
}
