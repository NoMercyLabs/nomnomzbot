// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { useMutation, type UseMutationReturnType } from '@tanstack/vue-query';
import { toValue, type MaybeRefOrGetter } from 'vue';

import { useOptimisticSave } from '@/composables/useOptimisticSave';

import { channelQuery, failed, refusal } from './channelQuery';
import { client } from './client';
import type { components } from './schema';

export type ChatSettings = components['schemas']['ChatSettingsDto'];
export type RaidAssessment = components['schemas']['RaidAssessmentDto'];

const version = '1';
const settingsPath = '/api/v{version}/channels/{channelId}/chat/settings';
const raidsPath = '/api/v{version}/channels/{channelId}/moderation/raid-assessments';
const massBanPath = '/api/v{version}/channels/{channelId}/moderation/actions/mass-ban';
const clearPath = '/api/v{version}/channels/{channelId}/moderation/chat/clear';

export const chatSettingsKey = (id: string | null): unknown[] => ['channels', id, 'chat-settings'];
export const raidAssessmentsKey = (id: string | null): unknown[] => ['channels', id, 'raid-assessments'];

async function readSettings(channelId: string): Promise<ChatSettings> {
  const { data, error, response } = await client.GET(settingsPath, {
    params: { path: { version, channelId } },
  });

  if (error !== undefined || !data?.data)
    throw failed('chat settings', response.status, error);

  return data.data;
}

export function useChatSettings(channelId: MaybeRefOrGetter<string | null>) {
  return channelQuery(channelId, chatSettingsKey, readSettings);
}

export function useSetChatSettings(channelId: MaybeRefOrGetter<string | null>) {
  return useOptimisticSave<ChatSettings, ChatSettings>({
    queryKey: () => chatSettingsKey(toValue(channelId)),
    describe: () => ({ key: 'saveChatModes' }),
    apply: (_current, settings) => settings,
    mutationFn: async (settings) => {
      const { data, error, response } = await client.PUT(settingsPath, {
        params: { path: { version, channelId: toValue(channelId) as string } },
        body: settings,
      });

      if (error !== undefined || !data?.data)
        throw refusal('chat settings', error, response.status);

      return data.data;
    },
  });
}

async function readRaids(channelId: string): Promise<RaidAssessment[]> {
  const assessments: RaidAssessment[] = [];
  let page = 1;
  let hasMore = true;

  while (hasMore) {
    const { data, error, response } = await client.GET(raidsPath, {
      params: { path: { version, channelId }, query: { Page: page, PageSize: 100 } },
    });

    if (error !== undefined)
      throw failed('raid assessments', response.status, error);

    assessments.push(...data?.data ?? []);
    hasMore = data?.hasMore === true;
    page += 1;
  }

  return assessments;
}

export function useRaidAssessments(channelId: MaybeRefOrGetter<string | null>) {
  return channelQuery(channelId, raidAssessmentsKey, readRaids);
}

export function useMassBanRaiders(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, string[], unknown> {
  return useMutation({
    mutationFn: async (ids: string[]): Promise<void> => {
      const { error, response } = await client.POST(massBanPath, {
        params: { path: { version, channelId: toValue(channelId) as string } },
        body: { targets: ids.map((twitchUserId) => ({ twitchUserId })) },
      });

      if (error !== undefined)
        throw failed('mass ban', response.status, error);
    },
  });
}

export function useClearChat(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, void, unknown> {
  return useMutation({
    mutationFn: async (): Promise<void> => {
      const { error, response } = await client.POST(clearPath, {
        params: { path: { version, channelId: toValue(channelId) as string } },
      });

      if (error !== undefined)
        throw failed('clear chat', response.status, error);
    },
  });
}
