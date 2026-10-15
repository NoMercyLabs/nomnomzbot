// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { useQuery, type UseQueryReturnType } from '@tanstack/vue-query';
import { computed, toValue, type MaybeRefOrGetter } from 'vue';

import type { KeyParts } from '@/lib/array';

import { channelQuery, failed } from './channelQuery';
import { client } from './client';
import type { components } from './schema';

export type ObsConnection = components['schemas']['ObsConnectionDto'];
export type ObsBridgeStatus = components['schemas']['ObsBridgeStatusDto'];
export type ObsState = components['schemas']['ObsStateDto'];
export type ObsScene = components['schemas']['ObsSceneDto'];

const version = '1';

export const obsConnectionKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'obs',
  'connection',
];
export const obsBridgeKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'obs',
  'bridge',
];
export const obsStateKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'obs',
  'state',
];
export const obsScenesKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'obs',
  'scenes',
];

// The channel's saved OBS connection; a channel with none stored reads back the defaults.
export function useObsConnection(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<ObsConnection | null, Error> {
  return channelQuery(
    channelId,
    obsConnectionKey,
    async (id: string): Promise<ObsConnection | null> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/channels/{channelId}/obs/connection',
        { params: { path: { version, channelId: id } } },
      );

      if (error !== undefined)
        throw failed('obs connection', response.status, error);

      return data?.data ?? null;
    },
  );
}

// How many browser-source bridges are open in OBS, and whether one of them leads.
export function useObsBridge(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<ObsBridgeStatus | null, Error> {
  return channelQuery(
    channelId,
    obsBridgeKey,
    async (id: string): Promise<ObsBridgeStatus | null> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/channels/{channelId}/obs/bridge/status',
        { params: { path: { version, channelId: id } } },
      );

      if (error !== undefined)
        throw failed('obs bridge', response.status, error);

      return data?.data ?? null;
    },
  );
}

// OBS answers the live reads only while the connection is switched on; a refusal is the "not reachable" state.
export function useObsState(
  channelId: MaybeRefOrGetter<string | null>,
  connected: MaybeRefOrGetter<boolean>,
): UseQueryReturnType<ObsState | null, Error> {
  return useQuery({
    queryKey: computed(() => obsStateKey(toValue(channelId))),
    queryFn: async (): Promise<ObsState | null> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/channels/{channelId}/obs/state',
        { params: { path: { version, channelId: toValue(channelId) as string } } },
      );

      if (error !== undefined)
        throw failed('obs state', response.status, error);

      return data?.data ?? null;
    },
    enabled: computed(() => toValue(channelId) !== null && toValue(connected)),
    retry: false,
  });
}

export function useObsScenes(
  channelId: MaybeRefOrGetter<string | null>,
  connected: MaybeRefOrGetter<boolean>,
): UseQueryReturnType<ObsScene[], Error> {
  return useQuery({
    queryKey: computed(() => obsScenesKey(toValue(channelId))),
    queryFn: async (): Promise<ObsScene[]> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/channels/{channelId}/obs/scenes',
        { params: { path: { version, channelId: toValue(channelId) as string } } },
      );

      if (error !== undefined)
        throw failed('obs scenes', response.status, error);

      return [...(data?.data ?? [])];
    },
    enabled: computed(() => toValue(channelId) !== null && toValue(connected)),
    retry: false,
  });
}
