// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

import {
  useMutation,
  useQuery,
  useQueryClient,
  type QueryClient,
  type UseMutationReturnType,
  type UseQueryReturnType,
} from '@tanstack/vue-query';
import { toValue, type MaybeRefOrGetter } from 'vue';

import type { KeyParts } from '@/lib/array';

import { myChannelsQuery, type ChannelSummary } from './channels';
import { channelQuery, failed, refusal } from './channelQuery';
import { client } from './client';
import { botStatusKey } from './home';
import type { components } from './schema';

// The channel the dashboard is open on, as the bot knows it: whether the bot is in it, and what leaving it costs.
export type ManagedChannel = components['schemas']['Channel'];
// What deleting the channel destroys, counted by the bot, and what stops working outside it.
export type ChannelDeletePreview = components['schemas']['ChannelDeletePreview'];

// What resetting the channel to defaults clears, counted by the bot.
export type ChannelResetPreview = components['schemas']['ChannelResetPreview'];

const version = '1';

export const channelInfoKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'channel-info',
];

// The caller's deleted channels are not per channel: the list is the same from every channel's page.
export const deletedChannelsKey = (): KeyParts => ['channels', 'deleted'];

async function readDeletedChannels(): Promise<ChannelSummary[]> {
  const { data, error, response } = await client.GET('/api/v{version}/channels', {
    params: { path: { version }, query: { deleted: true } },
  });

  if (error !== undefined)
    throw failed('deleted channels', response.status, error);

  // eslint-disable-next-line no-restricted-syntax -- the document types the rows as Channel, so the cast must go through the wire's true type
  return (data?.data ?? []) as unknown as ChannelSummary[];
}

// The channels the caller deleted that are still inside the restore window, with the deadline of each.
export function useDeletedChannels(): UseQueryReturnType<ChannelSummary[], Error> {
  return useQuery({ queryKey: deletedChannelsKey(), queryFn: readDeletedChannels });
}

async function readChannel(channelId: string): Promise<ManagedChannel> {
  const { data, error, response } = await client.GET('/api/v{version}/channels/{channelId}', {
    params: { path: { version, channelId } },
  });

  const channel = data?.data;

  if (error !== undefined || channel === undefined || channel === null)
    throw failed('channel info', response.status, error);

  return channel;
}

export function useChannelInfo(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<ManagedChannel, Error> {
  return channelQuery(channelId, channelInfoKey, readChannel);
}

// The bot decides its own presence, so the reader of this page sees the new state after the call, not before.
async function refreshPresence(queries: QueryClient, channelId: string | null): Promise<void> {
  await Promise.all([
    queries.invalidateQueries({ queryKey: channelInfoKey(channelId) }),
    queries.invalidateQueries({ queryKey: botStatusKey(channelId) }),
  ]);
}

export function useJoinChannel(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, void, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, void, undefined>({
    mutationFn: async (): Promise<void> => {
      const { error, response } = await client.POST('/api/v{version}/channels/{channelId}/join', {
        params: { path: { version, channelId: toValue(channelId) as string } },
      });

      if (error !== undefined)
        throw refusal('join channel', error, response.status);
    },
    onSuccess: () =>
      refreshPresence(queries, toValue(channelId)),
  });
}

export function useLeaveChannel(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, void, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, void, undefined>({
    mutationFn: async (): Promise<void> => {
      const { error, response } = await client.POST('/api/v{version}/channels/{channelId}/leave', {
        params: { path: { version, channelId: toValue(channelId) as string } },
      });

      if (error !== undefined)
        throw refusal('leave channel', error, response.status);
    },
    onSuccess: () =>
      refreshPresence(queries, toValue(channelId)),
  });
}

// The bot counts what the delete destroys before the confirm opens: a channel whose reach cannot be read is not offered for delete.
export async function fetchChannelDeletePreview(channelId: string): Promise<ChannelDeletePreview> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/delete-preview',
    { params: { path: { version, channelId } } },
  );

  const preview = data?.data;

  if (error !== undefined || preview === undefined || preview === null)
    throw failed('channel delete preview', response.status, error);

  return preview;
}

// The bot counts the settings a reset clears before the confirm opens, from the same scope the reset deletes through.
export async function fetchChannelResetPreview(channelId: string): Promise<ChannelResetPreview> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/reset-preview',
    { params: { path: { version, channelId } } },
  );

  const preview = data?.data;

  if (error !== undefined || preview === undefined || preview === null)
    throw failed('channel reset preview', response.status, error);

  return preview;
}

// A reset clears the channel's stored settings back to the defaults. It cannot be undone.
export function useResetChannel(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, void, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, void, undefined>({
    mutationFn: async (): Promise<void> => {
      const { error, response } = await client.POST('/api/v{version}/channels/{channelId}/reset', {
        params: { path: { version, channelId: toValue(channelId) as string } },
      });

      if (error !== undefined)
        throw refusal('reset channel', error, response.status);
    },
    onSuccess: () =>
      queries.invalidateQueries({ queryKey: ['channels', toValue(channelId)] }),
  });
}

// Both lists change with a delete or a restore: the channels the caller can open and the ones waiting to be restored.
async function refreshChannelLists(queries: QueryClient): Promise<void> {
  await Promise.all([
    queries.invalidateQueries({ queryKey: myChannelsQuery.queryKey }),
    queries.invalidateQueries({ queryKey: deletedChannelsKey() }),
  ]);
}

// A delete is soft: the bot keeps the channel for the restore window, then removes it for good.
export function useDeleteChannel(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, void, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, void, undefined>({
    mutationFn: async (): Promise<void> => {
      const { error, response } = await client.DELETE('/api/v{version}/channels/{channelId}', {
        params: { path: { version, channelId: toValue(channelId) as string } },
      });

      if (error !== undefined)
        throw refusal('delete channel', error, response.status);
    },
    onSuccess: () =>
      refreshChannelLists(queries),
  });
}

export function useRestoreChannel(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, string | undefined, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, string | undefined, undefined>({
    mutationFn: async (target): Promise<void> => {
      const { error, response } = await client.POST('/api/v{version}/channels/{channelId}/restore', {
        params: { path: { version, channelId: target ?? (toValue(channelId) as string) } },
      });

      if (error !== undefined)
        throw refusal('restore channel', error, response.status);
    },
    onSuccess: () =>
      refreshChannelLists(queries),
  });
}
