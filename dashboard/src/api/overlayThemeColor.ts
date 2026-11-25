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
  useQueryClient,
  type UseMutationReturnType,
  type UseQueryReturnType,
} from '@tanstack/vue-query';
import { toValue, type MaybeRefOrGetter } from 'vue';

import type { KeyParts } from '@/lib/array';

import { channelQuery, failed, refusal } from './channelQuery';
import { client } from './client';
import type { components } from './schema';

// The channel's one theme color for every overlay (the server key overlay.themeColor). An empty color means
// each overlay keeps the color of its own theme.
export type OverlayThemeColor = components['schemas']['OverlayThemeColor'];

export const overlayThemeColorKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'overlay-theme-color',
];

async function readThemeColor(): Promise<OverlayThemeColor | null> {
  const { data, error, response } = await client.GET('/api/v{version}/overlay-theme-color', {
    params: { path: { version: '1' } },
  });

  if (error !== undefined)
    throw failed('overlay theme color', response.status, error);

  return data?.data ?? null;
}

export function useOverlayThemeColor(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<OverlayThemeColor | null, Error> {
  return channelQuery(channelId, overlayThemeColorKey, readThemeColor);
}

// One write for both a new color and the reset: an empty color clears the key on the server.
export function useSaveOverlayThemeColor(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<OverlayThemeColor | null, Error, string, undefined> {
  const queries = useQueryClient();

  return useMutation<OverlayThemeColor | null, Error, string, undefined>({
    mutationFn: async (color: string): Promise<OverlayThemeColor | null> => {
      const { data, error, response } = await client.PUT('/api/v{version}/overlay-theme-color', {
        params: { path: { version: '1' } },
        body: { color },
      });

      if (error !== undefined)
        throw refusal('overlay theme color', error, response.status);

      return data?.data ?? null;
    },
    onSuccess: (saved: OverlayThemeColor | null) => {
      const key = overlayThemeColorKey(toValue(channelId));

      queries.setQueryData(key, saved);
    },
  });
}
