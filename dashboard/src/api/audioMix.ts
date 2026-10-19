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

// The channel's master and TTS volume. The server sends its own defaults and limits with them, so the
// dashboard keeps no copy of either.
export type ChannelAudioMix = components['schemas']['ChannelAudioMix'];
export type AudioMixChange = components['schemas']['UpdateChannelAudioMixRequest'];

// The two refusals the server gives for a volume outside its limits, each named after its field.
export const audioMixRefusals = {
  audio_mix_master_volume_out_of_range: 'masterVolume',
  audio_mix_tts_volume_out_of_range: 'ttsVolume',
} as const satisfies Record<string, keyof AudioMixChange>;

export const audioMixKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'audio-mix',
];

async function readAudioMix(): Promise<ChannelAudioMix | null> {
  const { data, error, response } = await client.GET('/api/v{version}/audio-mix', {
    params: { path: { version: '1' } },
  });

  if (error !== undefined)
    throw failed('audio mix', response.status, error);

  return data?.data ?? null;
}

export function useAudioMix(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<ChannelAudioMix | null, Error> {
  return channelQuery(channelId, audioMixKey, readAudioMix);
}

// One write for both volumes. The panel shows the saved values until the server accepts the change, so a
// refusal never leaves a number on screen that the bot does not hold.
export function useSaveAudioMix(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<ChannelAudioMix | null, Error, AudioMixChange, undefined> {
  const queries = useQueryClient();

  return useMutation<ChannelAudioMix | null, Error, AudioMixChange, undefined>({
    mutationFn: async (change: AudioMixChange): Promise<ChannelAudioMix | null> => {
      const { data, error, response } = await client.PUT('/api/v{version}/audio-mix', {
        params: { path: { version: '1' } },
        body: change,
      });

      if (error !== undefined)
        throw refusal('audio mix', error, response.status);

      return data?.data ?? null;
    },
    onSuccess: (saved: ChannelAudioMix | null) => {
      const key = audioMixKey(toValue(channelId));

      queries.setQueryData(key, saved);
    },
  });
}
