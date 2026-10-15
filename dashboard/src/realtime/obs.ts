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

import { obsScenesKey, obsStateKey, type ObsScene, type ObsState } from '@/api/obs';

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

function patchState(queries: QueryClient, channelId: string | null, change: ObsStateChangedPayload): void {
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

function patchScenes(queries: QueryClient, channelId: string | null, change: ObsStateChangedPayload): void {
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

// OBS pushes each live change with its new value; the loaded OBS reads take it in place, without a refetch.
export const obsStateChanged = {
  event: 'ObsStateChanged',
  handle: ({ queries, channelId }, change) => {
    patchScenes(queries, channelId, change);
    patchState(queries, channelId, change);
  },
} as const satisfies HubEventHandler<ObsStateChangedPayload>;

export const obsHandlers = [obsStateChanged] as const;
