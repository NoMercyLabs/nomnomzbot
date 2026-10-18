// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { useMutation, useQuery, useQueryClient, type UseQueryReturnType } from '@tanstack/vue-query';
import { computed, toValue, type MaybeRefOrGetter } from 'vue';

import type { KeyParts } from '@/lib/array';

import { failed } from './channelQuery';
import { client } from './client';
import { obsConnectionKey } from './obs';
import type { components } from './schema';

export type UpsertObsConnection = components['schemas']['UpsertObsConnectionRequest'];
export type ObsScreenshotFormat = 'png' | 'jpg';

const version = '1';

export const obsHotkeysKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'obs',
  'hotkeys',
];

// Save the OBS connection. The password is write only: null keeps the stored one, an empty string clears it.
export function useObsSaveConnection(channelId: MaybeRefOrGetter<string | null>) {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async (body: UpsertObsConnection): Promise<void> => {
      const { error, response } = await client.PUT(
        '/api/v{version}/channels/{channelId}/obs/connection',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body,
        },
      );

      if (error !== undefined)
        throw failed('obs save connection', response.status, error);
    },
    onSettled: () => queries.invalidateQueries({ queryKey: obsConnectionKey(toValue(channelId)) }),
  });
}

// The hotkey names OBS knows, read only while the link is on.
export function useObsHotkeys(
  channelId: MaybeRefOrGetter<string | null>,
  connected: MaybeRefOrGetter<boolean>,
): UseQueryReturnType<string[], Error> {
  return useQuery({
    queryKey: computed(() => obsHotkeysKey(toValue(channelId))),
    queryFn: async (): Promise<string[]> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/channels/{channelId}/obs/hotkeys',
        { params: { path: { version, channelId: toValue(channelId) as string } } },
      );

      if (error !== undefined)
        throw failed('obs hotkeys', response.status, error);

      return [...(data?.data ?? [])];
    },
    enabled: computed(() => toValue(channelId) !== null && toValue(connected)),
    retry: false,
  });
}

// Fire one OBS hotkey by name.
export function useObsTriggerHotkey(channelId: MaybeRefOrGetter<string | null>) {
  return useMutation({
    mutationFn: async (hotkeyName: string): Promise<void> => {
      const { error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/obs/hotkeys/trigger',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: { hotkeyName },
        },
      );

      if (error !== undefined)
        throw failed('obs hotkey', response.status, error);
    },
  });
}

// A still of one source as a data URI, never stored by the bot.
export function useObsScreenshot(channelId: MaybeRefOrGetter<string | null>) {
  return useMutation({
    mutationFn: async (input: {
      sourceName: string;
      imageFormat: ObsScreenshotFormat;
    }): Promise<string> => {
      const { data, error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/obs/source-screenshot',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: input,
        },
      );

      if (error !== undefined)
        throw failed('obs screenshot', response.status, error);

      return data?.data ?? '';
    },
  });
}
