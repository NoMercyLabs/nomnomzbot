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
  type UseQueryReturnType,
} from '@tanstack/vue-query';
import { toValue, type MaybeRefOrGetter } from 'vue';

import type { KeyParts } from '@/lib/array';

import { channelQuery, failed, refusal } from './channelQuery';
import { client } from './client';
import type { components } from './schema';

// The channel's chat filters: a regex, a list of blocked terms or a link policy, and what the bot does when a
// message matches. The list is the channel's own; the bot counts every match.
export type ChatFilter = components['schemas']['ChatFilter'];
export type ChatFilterType = components['schemas']['ChatFilterType'];
export type ChatFilterAction = components['schemas']['ChatFilterAction'];
export type ChatFilterRequest = components['schemas']['CreateChatFilterRequest'];
export type ChatFilterTest = components['schemas']['ChatFilterTestResult'];

export const CHAT_FILTER_TYPES = ['Regex', 'Blocklist', 'LinkPolicy'] as const satisfies readonly ChatFilterType[];

export const CHAT_FILTER_ACTIONS = [
  'Delete',
  'Timeout',
  'Hold',
  'Flag',
  'Escalate',
] as const satisfies readonly ChatFilterAction[];

const version = '1';
const pageSize = 100;

export const chatFiltersKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'chat-filters',
];

async function readFilters(channelId: string): Promise<ChatFilter[]> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/moderation/chat-filters',
    { params: { path: { version, channelId }, query: { Page: 1, PageSize: pageSize } } },
  );

  if (error !== undefined)
    throw failed('chat filters', response.status, error);

  return [...(data?.data ?? [])];
}

export function useChatFilters(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<ChatFilter[], Error> {
  return channelQuery(channelId, chatFiltersKey, readFilters);
}

// Every write ends the same way: the list is read again, so the page shows what the bot now holds.
function useRefreshFilters(channelId: MaybeRefOrGetter<string | null>): () => Promise<void> {
  const queries = useQueryClient();

  return () => queries.invalidateQueries({ queryKey: chatFiltersKey(toValue(channelId)) });
}

const onePath = '/api/v{version}/channels/{channelId}/moderation/chat-filters/{filterId}';

async function putFilter(
  channelId: string,
  filterId: string,
  body: components['schemas']['UpdateChatFilterRequest'],
): Promise<void> {
  const { error, response } = await client.PUT(onePath, {
    params: { path: {
      version,
      channelId,
      filterId,
    } },
    body,
  });

  if (error !== undefined)
    throw refusal('save chat filter', error, response.status);
}

export type SaveChatFilter = { existing: ChatFilter | null; request: ChatFilterRequest };

export function useSaveChatFilter(channelId: MaybeRefOrGetter<string | null>) {
  return useMutation({
    mutationFn: async ({ existing, request }: SaveChatFilter): Promise<void> => {
      if (existing)
        return putFilter(toValue(channelId) as string, existing.id, request);

      const { error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/moderation/chat-filters',
        { params: { path: { version, channelId: toValue(channelId) as string } }, body: request },
      );

      if (error !== undefined)
        throw refusal('save chat filter', error, response.status);
    },
    onSuccess: useRefreshFilters(channelId),
  });
}

// A switch on a row changes one field and leaves the rest as the bot holds it.
export type ToggleChatFilter = { filter: ChatFilter; isEnabled: boolean };

export function useToggleChatFilter(channelId: MaybeRefOrGetter<string | null>) {
  return useMutation({
    mutationFn: ({ filter, isEnabled }: ToggleChatFilter): Promise<void> =>
      putFilter(toValue(channelId) as string, filter.id, { isEnabled }),
    onSuccess: useRefreshFilters(channelId),
  });
}

export function useDeleteChatFilter(channelId: MaybeRefOrGetter<string | null>) {
  return useMutation({
    mutationFn: async (filter: ChatFilter): Promise<void> => {
      const { error, response } = await client.DELETE(onePath, {
        params: { path: {
          version,
          channelId: toValue(channelId) as string,
          filterId: filter.id,
        } },
      });

      if (error !== undefined)
        throw failed('delete chat filter', response.status, error);
    },
    onSuccess: useRefreshFilters(channelId),
  });
}

export type TestChatFilterInput = components['schemas']['TestChatFilterRequest'];

// A dry run against one sample message; nothing is saved.
export function useTestChatFilter(channelId: MaybeRefOrGetter<string | null>) {
  return useMutation({
    mutationFn: async (input: TestChatFilterInput): Promise<ChatFilterTest> => {
      const { data, error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/moderation/chat-filters/test',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: input,
        },
      );

      if (error !== undefined || !data?.data)
        throw failed('test chat filter', response.status, error);

      return data.data;
    },
  });
}
