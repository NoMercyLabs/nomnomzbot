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
  useQuery,
  useQueryClient,
  type QueryClient,
  type UseQueryReturnType,
} from '@tanstack/vue-query';
import { computed, toValue, type MaybeRefOrGetter } from 'vue';

import type { KeyParts } from '@/lib/array';

import { channelOptions, channelQuery, failed } from './channelQuery';
import { client } from './client';
import type { components } from './schema';

export type ObsConnection = components['schemas']['ObsConnection'];
export type ObsConnectionStatus = components['schemas']['ObsConnectionStatus'];
export type ObsBridgeStatus = components['schemas']['ObsBridgeStatus'];
export type ObsState = components['schemas']['ObsState'];
export type ObsScene = components['schemas']['ObsScene'];

const version = '1';

export const obsConnectionKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'obs',
  'connection',
];
export const obsConnectionStatusKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'obs',
  'connection',
  'status',
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
  return channelQuery(channelId, obsConnectionKey, readObsConnection);
}

async function readObsConnection(id: string): Promise<ObsConnection | null> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/obs/connection',
    { params: { path: { version, channelId: id }, query: { view: 'Full' } } },
  );

  if (error !== undefined)
    throw failed('obs connection', response.status, error);

  return (data?.data as ObsConnection | null | undefined) ?? null;
}

// Whether OBS is switched on, and nothing of its configuration; the moderator panels run on this (obs:control).
export function useObsConnectionStatus(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<ObsConnectionStatus | null, Error> {
  return channelQuery(channelId, obsConnectionStatusKey, readObsConnectionStatus);
}

async function readObsConnectionStatus(id: string): Promise<ObsConnectionStatus | null> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/obs/connection',
    { params: { path: { version, channelId: id }, query: { view: 'Status' } } },
  );

  if (error !== undefined)
    throw failed('obs connection status', response.status, error);

  return data?.data ?? null;
}

export const obsConnectionOptions = (id: string) =>
  channelOptions(obsConnectionKey, readObsConnection, id);

// How many browser-source bridges are open in OBS, and whether one of them leads.
export function useObsBridge(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<ObsBridgeStatus | null, Error> {
  return channelQuery(channelId, obsBridgeKey, readObsBridge);
}

async function readObsBridge(id: string): Promise<ObsBridgeStatus | null> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/obs/bridge/status',
    { params: { path: { version, channelId: id } } },
  );

  if (error !== undefined)
    throw failed('obs bridge', response.status, error);

  return data?.data ?? null;
}

export function obsBridgeOptions(id: string) {
  return channelOptions(obsBridgeKey, readObsBridge, id);
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

export type ObsTransition = components['schemas']['ObsTransition'];
export type ObsInput = components['schemas']['ObsInput'];

export const obsInputsKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'obs',
  'inputs',
];

export const obsStudioKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'obs',
  'studio-mode',
];
export const obsTransitionsKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'obs',
  'transitions',
];

// Whether OBS is in studio mode (a preview scene next to the program scene).
export function useObsStudioMode(
  channelId: MaybeRefOrGetter<string | null>,
  connected: MaybeRefOrGetter<boolean>,
): UseQueryReturnType<boolean, Error> {
  return useQuery({
    queryKey: computed(() => obsStudioKey(toValue(channelId))),
    queryFn: async (): Promise<boolean> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/channels/{channelId}/obs/studio-mode',
        { params: { path: { version, channelId: toValue(channelId) as string } } },
      );

      if (error !== undefined)
        throw failed('obs studio mode', response.status, error);

      return data?.data?.enabled === true;
    },
    enabled: computed(() => toValue(channelId) !== null && toValue(connected)),
    retry: false,
  });
}

// The transitions OBS knows, with the active one flagged.
export function useObsTransitions(
  channelId: MaybeRefOrGetter<string | null>,
  connected: MaybeRefOrGetter<boolean>,
): UseQueryReturnType<ObsTransition[], Error> {
  return useQuery({
    queryKey: computed(() => obsTransitionsKey(toValue(channelId))),
    queryFn: async (): Promise<ObsTransition[]> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/channels/{channelId}/obs/scene-transitions',
        { params: { path: { version, channelId: toValue(channelId) as string } } },
      );

      if (error !== undefined)
        throw failed('obs transitions', response.status, error);

      return [...(data?.data ?? [])];
    },
    enabled: computed(() => toValue(channelId) !== null && toValue(connected)),
    retry: false,
  });
}

export type Rollback<T> = { key: KeyParts; previous: T | undefined };

// Writes a new value into a cached read right away and hands back the old one for the rollback.
export async function applyOptimistic<T>(
  queries: QueryClient,
  key: KeyParts,
  update: (old: T) => T,
): Promise<Rollback<T>> {
  await queries.cancelQueries({ queryKey: key });

  const previous = queries.getQueryData<T>(key);

  if (previous !== undefined) {
    queries.setQueryData<T>(key, update(previous));
  }

  return { key, previous };
}

const scenesKey = (channelId: MaybeRefOrGetter<string | null>): KeyParts =>
  obsScenesKey(toValue(channelId));
const studioKey = (channelId: MaybeRefOrGetter<string | null>): KeyParts =>
  obsStudioKey(toValue(channelId));
const transitionsKey = (channelId: MaybeRefOrGetter<string | null>): KeyParts =>
  obsTransitionsKey(toValue(channelId));

// The same list with exactly one entry flagged as the current one.
function markCurrent<T extends { name: string; isCurrent: boolean }>(
  list: readonly T[],
  name: string,
): T[] {
  return list.map((item: T) => ({ ...item, isCurrent: item.name === name }));
}

export function rollBack<T>(queries: QueryClient, context: Rollback<T> | undefined): void {
  if (context?.previous !== undefined) {
    queries.setQueryData<T>(context.key, context.previous);
  }
}

// Switch the program scene. The list marks the new scene at once; a refusal puts the old mark back.
export function useObsSwitchScene(channelId: MaybeRefOrGetter<string | null>) {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async (scene: string): Promise<void> => {
      const { error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/obs/scene',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: { scene },
        },
      );

      if (error !== undefined)
        throw failed('obs switch scene', response.status, error);
    },
    onMutate: (scene: string) =>
      applyOptimistic<ObsScene[]>(queries, scenesKey(channelId), (old) =>
        markCurrent(old, scene)),
    onError: (_error, _scene, context) => rollBack(queries, context),
    onSettled: () => queries.invalidateQueries({ queryKey: obsScenesKey(toValue(channelId)) }),
  });
}

// Studio mode on or off.
export function useObsSetStudioMode(channelId: MaybeRefOrGetter<string | null>) {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async (enabled: boolean): Promise<void> => {
      const { error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/obs/studio-mode',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: { enabled },
        },
      );

      if (error !== undefined)
        throw failed('obs studio mode', response.status, error);
    },
    onMutate: (enabled: boolean) =>
      applyOptimistic<boolean>(queries, studioKey(channelId), () =>
        enabled),
    onError: (_error, _enabled, context) => rollBack(queries, context),
    onSettled: () => queries.invalidateQueries({ queryKey: obsStudioKey(toValue(channelId)) }),
  });
}

// Queue a scene as the studio preview.
export function useObsSetPreviewScene(channelId: MaybeRefOrGetter<string | null>) {
  return useMutation({
    mutationFn: async (scene: string): Promise<void> => {
      const { error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/obs/scene/preview',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: { scene },
        },
      );

      if (error !== undefined)
        throw failed('obs preview scene', response.status, error);
    },
  });
}

// Cut the preview scene to program. A null duration keeps the transition's own length.
export function useObsStudioTransition(channelId: MaybeRefOrGetter<string | null>) {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async (durationMs: number | null): Promise<void> => {
      const { error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/obs/studio-mode/transition',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: { durationMs },
        },
      );

      if (error !== undefined)
        throw failed('obs studio transition', response.status, error);
    },
    onSettled: () => queries.invalidateQueries({ queryKey: obsScenesKey(toValue(channelId)) }),
  });
}

// Make one transition the active one.
export function useObsSetTransition(channelId: MaybeRefOrGetter<string | null>) {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async ({ transitionName }: { transitionName: string }): Promise<void> => {
      const { error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/obs/scene-transitions/current',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: { transitionName },
        },
      );

      if (error !== undefined)
        throw failed('obs set transition', response.status, error);
    },
    onMutate: ({ transitionName }: { transitionName: string }) =>
      applyOptimistic<ObsTransition[]>(queries, transitionsKey(channelId), (old) =>
        markCurrent(old, transitionName)),
    onError: (_error, _name, context) => rollBack(queries, context),
    onSettled: () => queries.invalidateQueries({ queryKey: obsTransitionsKey(toValue(channelId)) }),
  });
}

// The OBS inputs with their live mute and level. An input without audio reads back null for both.
export function useObsInputs(
  channelId: MaybeRefOrGetter<string | null>,
  connected: MaybeRefOrGetter<boolean>,
): UseQueryReturnType<ObsInput[], Error> {
  return useQuery({
    queryKey: computed(() => obsInputsKey(toValue(channelId))),
    queryFn: async (): Promise<ObsInput[]> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/channels/{channelId}/obs/inputs',
        { params: { path: { version, channelId: toValue(channelId) as string } } },
      );

      if (error !== undefined)
        throw failed('obs inputs', response.status, error);

      return [...(data?.data ?? [])];
    },
    enabled: computed(() => toValue(channelId) !== null && toValue(connected)),
    retry: false,
  });
}

const inputsKey = (channelId: MaybeRefOrGetter<string | null>): KeyParts =>
  obsInputsKey(toValue(channelId));

// The same list with one input changed.
function changeInput(
  list: readonly ObsInput[],
  name: string,
  change: Partial<ObsInput>,
): ObsInput[] {
  return list.map((item: ObsInput) =>
    item.name === name
      ? { ...item, ...change }
      : item);
}

// Mute or unmute one input. The row flips at once; a refusal flips it back.
export function useObsSetInputMute(channelId: MaybeRefOrGetter<string | null>) {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async (input: { inputName: string; muted: boolean }): Promise<void> => {
      const { error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/obs/inputs/mute',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: input,
        },
      );

      if (error !== undefined)
        throw failed('obs input mute', response.status, error);
    },
    onMutate: (input: { inputName: string; muted: boolean }) =>
      applyOptimistic<ObsInput[]>(queries, inputsKey(channelId), (old) =>
        changeInput(old, input.inputName, { muted: input.muted })),
    onError: (_error, _input, context) => rollBack(queries, context),
  });
}

// Set one input's level in dB. The slider keeps the new level; a refusal puts the old one back.
export function useObsSetInputVolume(channelId: MaybeRefOrGetter<string | null>) {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async (input: { inputName: string; volumeDb: number }): Promise<void> => {
      const { error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/obs/inputs/volume',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: input,
        },
      );

      if (error !== undefined)
        throw failed('obs input volume', response.status, error);
    },
    onMutate: (input: { inputName: string; volumeDb: number }) =>
      applyOptimistic<ObsInput[]>(queries, inputsKey(channelId), (old) =>
        changeInput(old, input.inputName, { volumeDb: input.volumeDb })),
    onError: (_error, _input, context) => rollBack(queries, context),
  });
}
