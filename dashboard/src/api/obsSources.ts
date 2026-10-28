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
  useQueries,
  useQuery,
  useQueryClient,
  type UseQueryReturnType,
} from '@tanstack/vue-query';
import { computed, toValue, type ComputedRef, type MaybeRefOrGetter } from 'vue';

import type { KeyParts } from '@/lib/array';

import { failed } from './channelQuery';
import { client } from './client';
import { applyOptimistic, rollBack, useObsScenes } from './obs';
import type { components } from './schema';

export type ObsSceneItem = components['schemas']['ObsSceneItem'];
export type ObsFilter = components['schemas']['ObsFilter'];

export type SceneItemToggle = {
  sceneName: string;
  sceneItemId: number;
  value: boolean;
};

export type FilterToggle = {
  sourceName: string;
  filterName: string;
  enabled: boolean;
};

const version = '1';

export const obsSceneItemsKey = (channelId: string | null, sceneName: string | null): KeyParts => [
  'channels',
  channelId,
  'obs',
  'scene-items',
  sceneName,
];

export const obsSourceFiltersKey = (
  channelId: string | null,
  sourceName: string | null,
): KeyParts => [
  'channels',
  channelId,
  'obs',
  'source-filters',
  sourceName,
];

// The items placed in one scene, each with its shown and locked flags.
export function useObsSceneItems(
  channelId: MaybeRefOrGetter<string | null>,
  sceneName: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<ObsSceneItem[], Error> {
  return useQuery({
    queryKey: computed(() => obsSceneItemsKey(toValue(channelId), toValue(sceneName))),
    queryFn: async (): Promise<ObsSceneItem[]> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/channels/{channelId}/obs/scene-items',
        {
          params: {
            path: { version, channelId: toValue(channelId) as string },
            query: { sceneName: toValue(sceneName) as string },
          },
        },
      );

      if (error !== undefined)
        throw failed('obs scene items', response.status, error);

      return [...(data?.data ?? [])];
    },
    enabled: computed(() => toValue(channelId) !== null && toValue(sceneName) !== null),
    retry: false,
  });
}

export type ObsSceneItemPlace = {
  sceneName: string;
  sourceName: string;
};

// Every scene's items, looked up by item number. A number can sit in more than one scene, so each holds a list.
export function useObsSceneItemPlaces(
  channelId: MaybeRefOrGetter<string | null>,
  connected: MaybeRefOrGetter<boolean>,
): ComputedRef<Map<string, ObsSceneItemPlace[]>> {
  const scenes = useObsScenes(channelId, connected);

  const lists = useQueries({
    queries: computed(() =>
      (scenes.data.value ?? []).map((scene) => ({
        queryKey: obsSceneItemsKey(toValue(channelId), scene.name),
        queryFn: async (): Promise<ObsSceneItem[]> => {
          const { data, error, response } = await client.GET(
            '/api/v{version}/channels/{channelId}/obs/scene-items',
            {
              params: {
                path: { version, channelId: toValue(channelId) as string },
                query: { sceneName: scene.name },
              },
            },
          );

          if (error !== undefined)
            throw failed('obs scene items', response.status, error);

          return [...(data?.data ?? [])];
        },
        retry: false,
      })),
    ),
  });

  return computed(() => {
    const places = new Map<string, ObsSceneItemPlace[]>();

    (scenes.data.value ?? []).forEach((scene, index) => {
      lists.value[index]?.data?.forEach((item) => {
        const id = String(item.sceneItemId);
        const place = { sceneName: scene.name, sourceName: item.sourceName };

        places.set(id, [...(places.get(id) ?? []), place]);
      });
    });

    return places;
  });
}

// The filters on one source, read only while that source is open.
export function useObsSourceFilters(
  channelId: MaybeRefOrGetter<string | null>,
  sourceName: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<ObsFilter[], Error> {
  return useQuery({
    queryKey: computed(() => obsSourceFiltersKey(toValue(channelId), toValue(sourceName))),
    queryFn: async (): Promise<ObsFilter[]> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/channels/{channelId}/obs/source-filters',
        {
          params: {
            path: { version, channelId: toValue(channelId) as string },
            query: { sourceName: toValue(sourceName) as string },
          },
        },
      );

      if (error !== undefined)
        throw failed('obs source filters', response.status, error);

      return [...(data?.data ?? [])];
    },
    enabled: computed(() => toValue(channelId) !== null && toValue(sourceName) !== null),
    retry: false,
  });
}

type SceneItemFlag = 'enabled' | 'locked';

const flagRoutes = {
  enabled: '/api/v{version}/channels/{channelId}/obs/scene-items/enabled',
  locked: '/api/v{version}/channels/{channelId}/obs/scene-items/locked',
} as const satisfies Record<SceneItemFlag, string>;

async function postSceneItemFlag(
  channelId: string,
  flag: SceneItemFlag,
  toggle: SceneItemToggle,
): Promise<void> {
  const params = { path: { version, channelId } };
  const item = {
    sceneName: toggle.sceneName,
    sceneItemId: toggle.sceneItemId,
  };
  const { error, response } = flag === 'enabled'
    ? await client.POST(flagRoutes.enabled, {
        params,
        body: {
          ...item,
          enabled: toggle.value,
        },
      })
    : await client.POST(flagRoutes.locked, {
        params,
        body: {
          ...item,
          locked: toggle.value,
        },
      });

  if (error !== undefined)
    throw failed(`obs scene item ${flag}`, response.status, error);
}

function setFlag(
  items: readonly ObsSceneItem[],
  flag: SceneItemFlag,
  toggle: SceneItemToggle,
): ObsSceneItem[] {
  return items.map((item) =>
    item.sceneItemId === toggle.sceneItemId
      ? {
          ...item,
          [flag]: toggle.value,
        }
      : item);
}

// Show/hide or lock/unlock one scene item. The row flips at once; a refusal flips it back.
export function useObsSceneItemFlag(
  channelId: MaybeRefOrGetter<string | null>,
  flag: SceneItemFlag,
) {
  const queries = useQueryClient();
  const keyOf = (toggle: SceneItemToggle): KeyParts =>
    obsSceneItemsKey(toValue(channelId), toggle.sceneName);

  return useMutation({
    mutationFn: (toggle: SceneItemToggle) =>
      postSceneItemFlag(toValue(channelId) as string, flag, toggle),
    onMutate: (toggle: SceneItemToggle) =>
      applyOptimistic<ObsSceneItem[]>(queries, keyOf(toggle), (old) =>
        setFlag(old, flag, toggle)),
    onError: (_error, _toggle, context) => rollBack(queries, context),
    onSettled: (_data, _error, toggle) => queries.invalidateQueries({ queryKey: keyOf(toggle) }),
  });
}

// Switch one filter on a source on or off, with the same flip-and-roll-back.
export function useObsFilterEnabled(channelId: MaybeRefOrGetter<string | null>) {
  const queries = useQueryClient();
  const keyOf = (toggle: FilterToggle): KeyParts =>
    obsSourceFiltersKey(toValue(channelId), toggle.sourceName);

  return useMutation({
    mutationFn: async (toggle: FilterToggle): Promise<void> => {
      const { error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/obs/source-filters/enabled',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: toggle,
        },
      );

      if (error !== undefined)
        throw failed('obs filter enabled', response.status, error);
    },
    onMutate: (toggle: FilterToggle) =>
      applyOptimistic<ObsFilter[]>(queries, keyOf(toggle), (old) =>
        old.map((filter) =>
          filter.name === toggle.filterName
            ? {
                ...filter,
                enabled: toggle.enabled,
              }
            : filter)),
    onError: (_error, _toggle, context) => rollBack(queries, context),
    onSettled: (_data, _error, toggle) => queries.invalidateQueries({ queryKey: keyOf(toggle) }),
  });
}
