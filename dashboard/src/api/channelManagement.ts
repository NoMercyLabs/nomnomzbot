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
  type QueryClient,
  type UseMutationReturnType,
  type UseQueryReturnType,
} from '@tanstack/vue-query';
import { toValue, type MaybeRefOrGetter } from 'vue';

import type { KeyParts } from '@/lib/array';

import { myChannelsQuery } from './channels';
import { channelQuery, failed, refusal } from './channelQuery';
import { client } from './client';
import { botStatusKey } from './home';
import type { components } from './schema';

// The channel the dashboard is open on, as the bot knows it: whether the bot is in it, and what leaving it costs.
export type ManagedChannel = components['schemas']['Channel'];
// What deleting the channel destroys, counted by the bot, and what stops working outside it.
export type ChannelDeletePreview = components['schemas']['ChannelDeletePreview'];

const version = '1';

export const channelInfoKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'channel-info',
];

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
      queries.invalidateQueries({ queryKey: myChannelsQuery.queryKey }),
  });
}

export function useRestoreChannel(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, void, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, void, undefined>({
    mutationFn: async (): Promise<void> => {
      const { error, response } = await client.POST('/api/v{version}/channels/{channelId}/restore', {
        params: { path: { version, channelId: toValue(channelId) as string } },
      });

      if (error !== undefined)
        throw refusal('restore channel', error, response.status);
    },
    onSuccess: () =>
      queries.invalidateQueries({ queryKey: myChannelsQuery.queryKey }),
  });
}
