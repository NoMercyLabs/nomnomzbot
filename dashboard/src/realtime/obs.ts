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

import type { QueryClient } from '@tanstack/vue-query';

import {
  obsInputsKey,
  obsScenesKey,
  obsStateKey,
  type ObsInput,
  type ObsScene,
  type ObsState,
} from '@/api/obs';
import { obsVirtualCamKey } from '@/api/obsOutputs';
import {
  obsSceneItemsKey,
  obsSourceFiltersKey,
  type ObsFilter,
  type ObsSceneItem,
} from '@/api/obsSources';

import type { HubEventHandler } from './hubEvent';
import type { ObsStateChangedPayload } from './payloads';

type StatePatch = (change: ObsStateChangedPayload, state: ObsState) => Partial<ObsState>;

// A push carries no timecode: a start begins the timer at zero, a stop clears it, and a change while the
// output already runs (a pause, a resume) keeps what the page has.
const startOfTimer = '00:00:00.000';

function timecodeAfter(
  active: boolean,
  wasActive: boolean,
  current: string | null | undefined,
): string | null {
  if (!active)
    return null;

  return wasActive
    ? current ?? null
    : startOfTimer;
}

// The fields of the OBS state read that each kind of change sets; kinds without a field here have no page data yet.
const statePatches: Partial<Record<ObsStateChangedPayload['kind'], StatePatch>> = {
  programScene: (change) => ({ currentScene: change.sceneName ?? null }),
  stream: (change, state) => ({
    streaming: change.active === true,
    streamTimecode: timecodeAfter(change.active === true, state.streaming, state.streamTimecode),
  }),
  record: (change, state) => ({
    recording: change.active === true,
    recordPaused: change.paused === true,
    recordTimecode: timecodeAfter(change.active === true, state.recording, state.recordTimecode),
  }),
  replayBuffer: (change) => ({ replayBufferActive: change.active === true }),
};

// The virtual camera has its own read; a push sets it in place.
function patchVirtualCam(
  queries: QueryClient,
  channelId: string | null,
  change: ObsStateChangedPayload,
): void {
  if (change.kind !== 'virtualCam') return;

  queries.setQueryData<boolean>(obsVirtualCamKey(channelId), change.active === true);
}

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
          ...patch(change, state),
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

  const apply = (input: ObsInput) => {
    if (input.name !== change.inputName)
      return input;

    return change.kind === 'inputMute'
      ? { ...input, muted: change.muted === true } as const satisfies ObsInput
      : { ...input, volumeDb: change.volumeDb ?? input.volumeDb } as const satisfies ObsInput;
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
    patchVirtualCam(queries, channelId, change);
    patchInputs(queries, channelId, change);
    patchSceneItems(queries, channelId, change);
    patchFilters(queries, channelId, change);
  },
} as const satisfies HubEventHandler<ObsStateChangedPayload>;

export const obsHandlers = [obsStateChanged] as const;
