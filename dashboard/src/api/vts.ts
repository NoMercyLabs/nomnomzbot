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
  type UseQueryReturnType,
} from '@tanstack/vue-query';
import { computed, toValue, type MaybeRefOrGetter } from 'vue';

import type { KeyParts } from '@/lib/array';

import { channelOptions, channelQuery, failed } from './channelQuery';
import { client } from './client';
import { refused } from './errors';
import type { components } from './schema';

export type VtsConnection = components['schemas']['VtsConnection'];
export type VtsModelInventory = components['schemas']['VtsModelInventory'];
export type UpsertVtsConnection = components['schemas']['UpsertVtsConnectionRequest'];

const version = '1';

export const vtsConnectionKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'vts',
  'connection',
];

export const vtsInventoryKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'vts',
  'inventory',
];

// The channel's saved VTube Studio connection; a channel with none stored reads back the defaults.
export function useVtsConnection(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<VtsConnection | null, Error> {
  return channelQuery(channelId, vtsConnectionKey, readVtsConnection);
}

async function readVtsConnection(id: string): Promise<VtsConnection | null> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/vts/connection',
    { params: { path: { version, channelId: id } } },
  );

  if (error !== undefined)
    throw failed('vts connection', response.status, error);

  return data?.data ?? null;
}

export const vtsConnectionOptions = (id: string) =>
  channelOptions(vtsConnectionKey, readVtsConnection, id);

// Models, hotkeys and expressions. VTube Studio answers only while it is linked, so the read waits for `ready`.
export function useVtsInventory(
  channelId: MaybeRefOrGetter<string | null>,
  ready: MaybeRefOrGetter<boolean>,
): UseQueryReturnType<VtsModelInventory | null, Error> {
  return useQuery({
    queryKey: computed(() => vtsInventoryKey(toValue(channelId))),
    queryFn: async (): Promise<VtsModelInventory | null> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/channels/{channelId}/vts/inventory',
        { params: { path: { version, channelId: toValue(channelId) as string } } },
      );

      if (error !== undefined)
        throw failed('vts inventory', response.status, error);

      return data?.data ?? null;
    },
    enabled: computed(() => toValue(channelId) !== null && toValue(ready)),
    retry: false,
  });
}

// Save the connection. The plugin token is never part of it: the authorize flow owns that.
export function useVtsSaveConnection(channelId: MaybeRefOrGetter<string | null>) {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async (body: UpsertVtsConnection): Promise<void> => {
      const { error, response } = await client.PUT(
        '/api/v{version}/channels/{channelId}/vts/connection',
        { params: { path: { version, channelId: toValue(channelId) as string } }, body },
      );

      if (error !== undefined)
        throw failed('vts save connection', response.status, error);
    },
    onSettled: () => queries.invalidateQueries({ queryKey: vtsConnectionKey(toValue(channelId)) }),
  });
}

// The old bridge credential stops working at once.
export function useVtsRotateBridgeToken(channelId: MaybeRefOrGetter<string | null>) {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async (): Promise<void> => {
      const { error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/vts/connection/rotate-bridge-token',
        { params: { path: { version, channelId: toValue(channelId) as string } } },
      );

      if (error !== undefined)
        throw failed('vts rotate bridge token', response.status, error);
    },
    onSettled: () => queries.invalidateQueries({ queryKey: vtsConnectionKey(toValue(channelId)) }),
  });
}

// Starts the one-time approval; VTube Studio shows its Allow dialog and this waits up to a minute for the click.
export function useVtsAuthorize(channelId: MaybeRefOrGetter<string | null>) {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async (): Promise<void> => {
      const { error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/vts/connection/authorize',
        { params: { path: { version, channelId: toValue(channelId) as string } } },
      );

      if (error !== undefined)
        throw failed('vts authorize', response.status, error);
    },
    onSettled: () => queries.invalidateQueries({ queryKey: vtsConnectionKey(toValue(channelId)) }),
  });
}

export type VtsControl = { requestType: string; payload: Record<string, string | boolean> };

// One VTube Studio request through the raw control route: load a model, trigger a hotkey, set an expression.
export function useVtsControl(channelId: MaybeRefOrGetter<string | null>) {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async ({ requestType, payload }: VtsControl): Promise<void> => {
      const { data, error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/vts/control',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: { requestType, payloadJson: JSON.stringify(payload) },
        },
      );

      if (error !== undefined)
        throw failed('vts control', response.status, error);

      if (data?.data?.ok === false)
        throw refused('vts control', data.data.error);
    },
    onSettled: () => queries.invalidateQueries({ queryKey: vtsInventoryKey(toValue(channelId)) }),
  });
}
