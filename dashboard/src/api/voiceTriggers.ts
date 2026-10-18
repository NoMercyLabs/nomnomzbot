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

// The voice triggers of the Triggers and Timers page: a spoken word that counts up and shows a sticker.
// The bot pushes a ConfigChanged "voice-triggers" on every write and every fire, so the list stays live.
export type VoiceTrigger = components['schemas']['VoiceTrigger'];
export type NewVoiceTrigger = components['schemas']['CreateVoiceTriggerRequest'];
export type VoiceTriggerChange = components['schemas']['UpdateVoiceTriggerRequest'];
export type ChannelAsset = components['schemas']['ChannelAsset'];

const version = '1';

export const voiceTriggersKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'voice-triggers',
];

export const voiceListenerLinkKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'voice-triggers',
  'listener-link',
];

export const stickerAssetsKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'assets',
  'stickers',
];

async function readTriggers(channelId: string): Promise<VoiceTrigger[]> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/voice-triggers',
    { params: { path: { version, channelId } } },
  );

  if (error !== undefined)
    throw failed('voice triggers', response.status, error);

  return [...(data?.data ?? [])];
}

async function readListenerLink(channelId: string): Promise<string> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/voice-triggers/listener-link',
    { params: { path: { version, channelId } } },
  );

  if (error !== undefined)
    throw failed('voice listener link', response.status, error);

  return data?.data ?? '';
}

// The channel's uploaded images, the sticker choices. 100 is the most one page returns.
async function readStickers(): Promise<ChannelAsset[]> {
  const { data, error, response } = await client.GET('/api/v{version}/assets', {
    params: { path: { version }, query: { PageSize: 100 } },
  });

  if (error !== undefined)
    throw failed('stickers', response.status, error);

  return (data?.data ?? []).filter((asset: ChannelAsset): boolean => asset.kind === 'image');
}

export function useVoiceTriggers(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<VoiceTrigger[], Error> {
  return channelQuery(channelId, voiceTriggersKey, readTriggers);
}

export function useVoiceListenerLink(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<string, Error> {
  return channelQuery(channelId, voiceListenerLinkKey, readListenerLink);
}

export function useStickerAssets(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<ChannelAsset[], Error> {
  return channelQuery(channelId, stickerAssetsKey, readStickers);
}

export function useCreateVoiceTrigger(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, NewVoiceTrigger, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, NewVoiceTrigger, undefined>({
    mutationFn: async (trigger: NewVoiceTrigger): Promise<void> => {
      const { error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/voice-triggers',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: trigger,
        },
      );

      if (error !== undefined)
        throw refusal('create voice trigger', error, response.status);
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: voiceTriggersKey(toValue(channelId)) });
    },
  });
}

export type VoiceTriggerEdit = { triggerId: string; change: VoiceTriggerChange };

// Only the changed fields travel in the PATCH. A refusal keeps the bot's reason on the mutation error.
export function useUpdateVoiceTrigger(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, VoiceTriggerEdit, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, VoiceTriggerEdit, undefined>({
    mutationFn: async ({ triggerId, change }): Promise<void> => {
      const { error, response } = await client.PATCH(
        '/api/v{version}/channels/{channelId}/voice-triggers/{triggerId}',
        {
          params: { path: {
            version,
            channelId: toValue(channelId) as string,
            triggerId,
          } },
          body: change,
        },
      );

      if (error !== undefined)
        throw refusal('save voice trigger', error, response.status);
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: voiceTriggersKey(toValue(channelId)) });
    },
  });
}

export function useDeleteVoiceTrigger(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, string, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, string, undefined>({
    mutationFn: async (triggerId: string): Promise<void> => {
      const { error, response } = await client.DELETE(
        '/api/v{version}/channels/{channelId}/voice-triggers/{triggerId}',
        {
          params: { path: {
            version,
            channelId: toValue(channelId) as string,
            triggerId,
          } },
        },
      );

      if (error !== undefined)
        throw refusal('delete voice trigger', error, response.status);
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: voiceTriggersKey(toValue(channelId)) });
    },
  });
}
