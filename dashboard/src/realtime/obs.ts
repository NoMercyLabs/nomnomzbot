// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { QueryClient } from '@tanstack/vue-query';

import {
  obsInputsKey,
  obsScenesKey,
  obsStateKey,
  type ObsInput,
  type ObsScene,
  type ObsState,
} from '@/api/obs';
import {
  obsSceneItemsKey,
  obsSourceFiltersKey,
  type ObsFilter,
  type ObsSceneItem,
} from '@/api/obsSources';

import type { HubEventHandler } from './hubEvent';
import type { ObsStateChangedPayload } from './payloads';

type StatePatch = (change: ObsStateChangedPayload) => Partial<ObsState>;

// The fields of the OBS state read that each kind of change sets; kinds without a field here have no page data yet.
const statePatches: Partial<Record<ObsStateChangedPayload['kind'], StatePatch>> = {
  programScene: (change) => ({ currentScene: change.sceneName ?? null }),
  stream: (change) => ({ streaming: change.active === true }),
  record: (change) => ({
    recording: change.active === true,
    recordPaused: change.paused === true,
  }),
  replayBuffer: (change) => ({ replayBufferActive: change.active === true }),
};

function markCurrent(scenes: readonly ObsScene[], current: string | null): ObsScene[] {
  return scenes.map((scene) => ({
    ...scene,
    isCurrent: scene.name === current,
  }));
}

function currentSceneOf(queries: QueryClient, channelId: string | null): string | null {
  const state = queries.getQueryData<ObsState | null>(obsStateKey(channelId));
  const scenes = queries.getQueryData<ObsScene[]>(obsScenesKey(channelId));
  const marked = scenes?.find((scene) => scene.isCurrent);

  return state?.currentScene ?? marked?.name ?? null;
}

function patchState(
  queries: QueryClient,
  channelId: string | null,
  change: ObsStateChangedPayload,
): void {
  const patch = statePatches[change.kind];

  if (!patch) return;

  queries.setQueryData<ObsState | null>(obsStateKey(channelId), (state) =>
    state
      ? {
          ...state,
          ...patch(change),
        }
      : state);
}

function patchScenes(
  queries: QueryClient,
  channelId: string | null,
  change: ObsStateChangedPayload,
): void {
  if (change.kind === 'programScene') {
    queries.setQueryData<ObsScene[]>(obsScenesKey(channelId), (scenes) =>
      scenes && markCurrent(scenes, change.sceneName ?? null));

    return;
  }

  if (change.kind !== 'sceneList' || !change.sceneNames) return;

  const current = currentSceneOf(queries, channelId);
  const listed = change.sceneNames.map((name) => ({
    name,
    isCurrent: false,
  }));

  queries.setQueryData<ObsScene[]>(obsScenesKey(channelId), (scenes) =>
    scenes && markCurrent(listed, current));
}

// An input mute or level change sets that one input in the loaded list.
function patchInputs(
  queries: QueryClient,
  channelId: string | null,
  change: ObsStateChangedPayload,
): void {
  if (change.kind !== 'inputMute' && change.kind !== 'inputVolume') return;

  const apply = (input: ObsInput): ObsInput => {
    if (input.name !== change.inputName)
      return input;

    return change.kind === 'inputMute'
      ? { ...input, muted: change.muted === true }
      : { ...input, volumeDb: change.volumeDb ?? input.volumeDb };
  };

  queries.setQueryData<ObsInput[]>(obsInputsKey(channelId), (inputs) =>
    inputs?.map(apply));
}

const sceneItemFlags = {
  sceneItemVisibility: (change) => ({ enabled: change.enabled === true }),
  sceneItemLock: (change) => ({ locked: change.locked === true }),
} as const satisfies Partial<Record<ObsStateChangedPayload['kind'], (change: ObsStateChangedPayload) => Partial<ObsSceneItem>>>;

function isSceneItemKind(kind: ObsStateChangedPayload['kind']): kind is keyof typeof sceneItemFlags {
  return kind in sceneItemFlags;
}

function patchSceneItems(
  queries: QueryClient,
  channelId: string | null,
  change: ObsStateChangedPayload,
): void {
  if (!isSceneItemKind(change.kind) || change.sceneItemId == null) return;

  const flags = sceneItemFlags[change.kind](change);
  const key = obsSceneItemsKey(channelId, change.sceneName ?? null);

  queries.setQueryData<ObsSceneItem[]>(key, (items) =>
    items?.map((item) =>
      item.sceneItemId === change.sceneItemId
        ? {
            ...item,
            ...flags,
          }
        : item));
}

function patchFilters(
  queries: QueryClient,
  channelId: string | null,
  change: ObsStateChangedPayload,
): void {
  if (change.kind !== 'sourceFilter') return;

  const key = obsSourceFiltersKey(channelId, change.sourceName ?? null);

  queries.setQueryData<ObsFilter[]>(key, (filters) =>
    filters?.map((filter) =>
      filter.name === change.filterName
        ? {
            ...filter,
            enabled: change.enabled === true,
          }
        : filter));
}

// OBS pushes each live change with its new value; the loaded OBS reads take it in place, without a refetch.
export const obsStateChanged = {
  event: 'ObsStateChanged',
  handle: ({ queries, channelId }, change) => {
    patchScenes(queries, channelId, change);
    patchState(queries, channelId, change);
    patchInputs(queries, channelId, change);
    patchSceneItems(queries, channelId, change);
    patchFilters(queries, channelId, change);
  },
} as const satisfies HubEventHandler<ObsStateChangedPayload>;

export const obsHandlers = [obsStateChanged] as const;
