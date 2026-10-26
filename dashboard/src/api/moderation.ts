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
  useInfiniteQuery,
  useMutation,
  useQuery,
  useQueryClient,
  type InfiniteData,
  type UseInfiniteQueryReturnType,
  type UseMutationReturnType,
  type UseQueryReturnType,
} from '@tanstack/vue-query';
import { computed, toValue, type MaybeRefOrGetter } from 'vue';

import { useOptimisticSave } from '@/composables/useOptimisticSave';
import { numberOrNull } from '@/lib/number';

import { failed } from './channelQuery';
import { client } from './client';
import { userNotesKey } from './people';
import type { components } from './schema';
import type { KeyParts } from '@/lib/array';

export type ModerationHistoryEntry = components['schemas']['ModerationHistoryEntry'];
export type ModerationStanding = components['schemas']['ModerationStanding'];

// The bot-side standings, mildest first. Normal is no standing at all.
export const STANDING_LEVELS = ['muted', 'shadowbanned', 'blacklisted'] as const satisfies string[];
export type StandingLevel = (typeof STANDING_LEVELS)[number];

export type ModerationHistoryPage = {
  entries: ModerationHistoryEntry[];
  hasMore: boolean;
  nextPage: number | null;
};

const version = '1';
const pageSize = 25;
const firstPage = 1;

export const moderationHistoryKey = (
  channelId: string | null,
  userId: string | null,
): KeyParts => ['moderation', channelId, 'history', userId];

async function readHistoryPage(
  channelId: string,
  userId: string,
  page: number,
): Promise<ModerationHistoryPage> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/moderation/history/{userId}',
    {
      params: {
        path: {
          version,
          channelId,
          userId,
        },
        query: { Page: page, PageSize: pageSize },
      },
    },
  );

  if (error !== undefined)
    throw failed('moderation history', response.status, error);

  return {
    entries: data?.data ?? [],
    hasMore: data?.hasMore ?? false,
    nextPage: numberOrNull(data?.nextPage),
  };
}

// One person's moderation history in this channel, newest first. The id is the internal one
// (CommunityRow.internalUserId); a null id, such as a caller without the read right, reads nothing.
export function useModerationHistory(
  channelId: MaybeRefOrGetter<string | null>,
  userId: MaybeRefOrGetter<string | null>,
): UseInfiniteQueryReturnType<InfiniteData<ModerationHistoryPage, number>, Error> {
  return useInfiniteQuery({
    queryKey: computed(() => moderationHistoryKey(toValue(channelId), toValue(userId))),
    queryFn: ({ pageParam }: { pageParam: number }): Promise<ModerationHistoryPage> =>
      readHistoryPage(toValue(channelId) as string, toValue(userId) as string, pageParam),
    initialPageParam: firstPage,
    getNextPageParam: (
      last: ModerationHistoryPage,
      all: ModerationHistoryPage[],
    ): number | undefined =>
      last.hasMore
        ? (last.nextPage ?? all.length + 1)
        : undefined,
    enabled: computed(() => toValue(channelId) !== null && toValue(userId) !== null),
  });
}

// Standings are kept per platform identity; the profile shows the person's Twitch one.
const provider = 'twitch';

export const standingKey = (channelId: string | null, userId: string | null): KeyParts => [
  'moderation',
  channelId,
  'standing',
  userId,
];

async function readStanding(channelId: string, userId: string): Promise<ModerationStanding | null> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/moderation/users/{userId}/context',
    {
      params: {
        path: {
          version,
          channelId,
          userId,
        },
      },
    },
  );

  if (error !== undefined)
    throw failed('moderation standing', response.status, error);

  const standings = data?.data?.standings ?? [];

  return standings.find((standing) =>
    standing.provider === provider) ?? null;
}

// The id is the Twitch user id; null (a caller without the read right) reads nothing.
export function useStanding(
  channelId: MaybeRefOrGetter<string | null>,
  userId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<ModerationStanding | null, Error> {
  return useQuery({
    queryKey: computed(() => standingKey(toValue(channelId), toValue(userId))),
    queryFn: () => readStanding(toValue(channelId) as string, toValue(userId) as string),
    enabled: computed(() => toValue(channelId) !== null && toValue(userId) !== null),
  });
}

// level null puts the person back to normal.
export type StandingChange = {
  userId: string;
  level: StandingLevel | null;
  reason: string | null;
};

async function writeStanding(channelId: string, change: StandingChange): Promise<void> {
  const path = {
    version,
    channelId,
    userId: change.userId,
  };
  const { error, response } = change.level === null
    ? await client.DELETE('/api/v{version}/channels/{channelId}/moderation/users/{userId}/standing', {
        params: { path, query: { provider } },
      })
    : await client.POST('/api/v{version}/channels/{channelId}/moderation/users/{userId}/standing', {
        params: { path },
        body: {
          provider,
          standing: change.level,
          reason: change.reason,
        },
      });

  if (error !== undefined)
    throw failed('moderation standing', response.status, error);
}

// The new standing shows at once. The server notes every change on the person, so the notes reload after it.
export function useSetStanding(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<ModerationStanding | null, Error, StandingChange, number> {
  const queries = useQueryClient();

  return useOptimisticSave<ModerationStanding | null, StandingChange>({
    describe: ({ userId, level }) => ({ key: level === null
      ? 'clearStanding'
      : 'setStanding', params: { userId } }),
    queryKey: ({ userId }: StandingChange) => standingKey(toValue(channelId), userId),
    mutationFn: async (change: StandingChange): Promise<ModerationStanding | null> => {
      const id = toValue(channelId) as string;

      await writeStanding(id, change);
      void queries.invalidateQueries({ queryKey: userNotesKey(id, change.userId) });

      return readStanding(id, change.userId);
    },
    apply: (
      _current: ModerationStanding | null | undefined,
      { userId, level, reason }: StandingChange,
    ) => {
      if (level === null)
        return null;

      return {
        userId,
        provider,
        standing: level,
        reason,
        updatedAt: new Date().toISOString(),
      } as const satisfies ModerationStanding;
    },
  });
}

// Twitch's own suspicious-user flag, not the bot standing above: watch a chatter, hold their messages, or clear it.
export const SUSPICIOUS_STATUSES = ['active_monitoring', 'restricted'] as const satisfies string[];
export type SuspiciousStatusKind = (typeof SUSPICIOUS_STATUSES)[number];
export type SuspiciousStatus = components['schemas']['SuspiciousStatus'];

// A status sets the flag; null clears it. Twitch answers with the flag it now holds.
export type SuspiciousChange = { userId: string; status: SuspiciousStatusKind | null };

export function useSuspiciousChange(channelId: MaybeRefOrGetter<string | null>) {
  return useMutation({
    mutationFn: async ({ userId, status }: SuspiciousChange): Promise<SuspiciousStatus | null> => {
      const path = { version, channelId: toValue(channelId) as string };
      const { data, error, response } = status === null
        ? await client.DELETE('/api/v{version}/channels/{channelId}/moderation/suspicious/{userId}', {
            params: { path: { ...path, userId } },
          })
        : await client.POST('/api/v{version}/channels/{channelId}/moderation/suspicious', {
            params: { path },
            body: { targetUserId: userId, status },
          });

      if (error !== undefined)
        throw failed('suspicious status', response.status, error);

      return data?.data ?? null;
    },
  });
}
