// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { QueryClient } from '@tanstack/vue-query';
import { beforeEach, describe, expect, it, vi } from 'vitest';

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

import type { ObsStateChangedPayload } from '../payloads';

import { FakeHubConnection, fakeSignalR } from './fakeSignalR';

vi.mock('@microsoft/signalr', (importOriginal) =>
  fakeSignalR(importOriginal));

const state = {
  currentScene: 'Main',
  streaming: false,
  recording: false,
  recordPaused: false,
  replayBufferActive: false,
  recordTimecode: null,
} as const satisfies ObsState;

const scenes: ObsScene[] = [
  {
    name: 'Main',
    isCurrent: true,
  },
  {
    name: 'BRB',
    isCurrent: false,
  },
];

const inputs: ObsInput[] = [
  {
    name: 'Mic',
    kind: 'wasapi_input_capture',
    muted: false,
    volumeDb: -6,
  },
  {
    name: 'Music',
    kind: 'wasapi_output_capture',
    muted: false,
    volumeDb: -20,
  },
];

async function started(): Promise<[QueryClient, FakeHubConnection]> {
  const hub = await import('../dashboardHub');
  const queries = new QueryClient();

  await hub.startDashboardHub(queries);
  await hub.watchChannel('c1');

  queries.setQueryData(obsStateKey('c1'), state);
  queries.setQueryData(obsScenesKey('c1'), scenes);
  queries.setQueryData(obsStateKey('other'), state);
  queries.setQueryData(obsInputsKey('c1'), inputs);
  queries.setQueryData(obsInputsKey('other'), inputs);

  return [queries, FakeHubConnection.at(FakeHubConnection.instances.length - 1)];
}

function send(
  connection: FakeHubConnection,
  payload: Omit<ObsStateChangedPayload, 'broadcasterId' | 'timestamp'>,
): void {
  connection.receive('ObsStateChanged', {
    broadcasterId: 'c1',
    timestamp: '2026-10-15T20:00:00Z',
    ...payload,
  });
}

beforeEach(() => {
  vi.resetModules();
  FakeHubConnection.instances = [];
});

describe('ObsStateChanged patches the OBS caches in place', () => {
  it('a program scene change moves the current scene in the state and the scene list', async () => {
    const [queries, connection] = await started();

    send(connection, {
      kind: 'programScene',
      sceneName: 'BRB',
    });

    expect(queries.getQueryData<ObsState>(obsStateKey('c1'))?.currentScene).toBe('BRB');

    expect(queries.getQueryData<ObsScene[]>(obsScenesKey('c1'))).toEqual([
      {
        name: 'Main',
        isCurrent: false,
      },
      {
        name: 'BRB',
        isCurrent: true,
      },
    ]);

    expect(queries.getQueryData<ObsState>(obsStateKey('other'))?.currentScene).toBe('Main');
    expect(queries.getQueryState(obsScenesKey('c1'))?.isInvalidated).toBe(false);
  });

  it('stream, record and replay buffer changes set their flags', async () => {
    const [queries, connection] = await started();

    send(connection, {
      kind: 'stream',
      active: true,
    });

    send(connection, {
      kind: 'record',
      active: true,
      paused: true,
    });

    send(connection, {
      kind: 'replayBuffer',
      active: true,
    });

    expect(queries.getQueryData<ObsState>(obsStateKey('c1'))).toEqual({
      ...state,
      streaming: true,
      streamTimecode: '00:00:00.000',
      recording: true,
      recordPaused: true,
      recordTimecode: '00:00:00.000',
      replayBufferActive: true,
    });
  });

  it('a stop clears the timer and a pause keeps the one the page has', async () => {
    const [queries, connection] = await started();

    queries.setQueryData(obsStateKey('c1'), {
      ...state,
      streaming: true,
      streamTimecode: '00:10:00.000',
      recording: true,
      recordTimecode: '00:20:00.000',
    });

    send(connection, {
      kind: 'record',
      active: true,
      paused: true,
    });

    send(connection, {
      kind: 'stream',
      active: false,
    });

    const after = queries.getQueryData<ObsState>(obsStateKey('c1'));

    expect(after?.recordTimecode).toBe('00:20:00.000');
    expect(after?.streamTimecode).toBeNull();
  });

  it('a virtual camera change sets the virtual camera read of that channel only', async () => {
    const [queries, connection] = await started();

    queries.setQueryData(obsVirtualCamKey('c1'), false);
    queries.setQueryData(obsVirtualCamKey('other'), false);

    send(connection, {
      kind: 'virtualCam',
      active: true,
    });

    expect(queries.getQueryData<boolean>(obsVirtualCamKey('c1'))).toBe(true);
    expect(queries.getQueryData<boolean>(obsVirtualCamKey('other'))).toBe(false);

    send(connection, {
      kind: 'virtualCam',
      active: false,
    });

    expect(queries.getQueryData<boolean>(obsVirtualCamKey('c1'))).toBe(false);
  });

  it('a scene list change replaces the list and keeps the live scene marked', async () => {
    const [queries, connection] = await started();

    send(connection, {
      kind: 'sceneList',
      sceneNames: ['Intro', 'Main'],
    });

    expect(queries.getQueryData<ObsScene[]>(obsScenesKey('c1'))).toEqual([
      {
        name: 'Intro',
        isCurrent: false,
      },
      {
        name: 'Main',
        isCurrent: true,
      },
    ]);
  });

  it('an input mute change sets the muted flag of that input only', async () => {
    const [queries, connection] = await started();

    send(connection, {
      kind: 'inputMute',
      inputName: 'Mic',
      muted: true,
    });

    const cached = queries.getQueryData<ObsInput[]>(obsInputsKey('c1'));

    expect(cached?.[0]).toEqual({ ...inputs[0], muted: true });
    expect(cached?.[1]).toEqual(inputs[1]);
    expect(queries.getQueryData<ObsInput[]>(obsInputsKey('other'))).toEqual(inputs);
    expect(queries.getQueryState(obsInputsKey('c1'))?.isInvalidated).toBe(false);
  });

  it('an input volume change sets the dB of that input only', async () => {
    const [queries, connection] = await started();

    send(connection, {
      kind: 'inputVolume',
      inputName: 'Music',
      volumeDb: -35.5,
    });

    const cached = queries.getQueryData<ObsInput[]>(obsInputsKey('c1'));

    expect(cached?.[1]).toEqual({ ...inputs[1], volumeDb: -35.5 });
    expect(cached?.[0]).toEqual(inputs[0]);
  });

  it('a change for a page that is not loaded leaves the cache empty', async () => {
    const hub = await import('../dashboardHub');
    const queries = new QueryClient();

    await hub.startDashboardHub(queries);
    await hub.watchChannel('c1');

    send(FakeHubConnection.at(FakeHubConnection.instances.length - 1), {
      kind: 'programScene',
      sceneName: 'BRB',
    });

    expect(queries.getQueryData(obsStateKey('c1'))).toBeUndefined();
    expect(queries.getQueryData(obsScenesKey('c1'))).toBeUndefined();
  });
});

describe('ObsStateChanged patches the scene items and filters in place', () => {
  const items: ObsSceneItem[] = [
    {
      sceneItemId: 1,
      sourceName: 'Webcam',
      enabled: true,
      locked: false,
    },
    {
      sceneItemId: 2,
      sourceName: 'Overlay',
      enabled: true,
      locked: false,
    },
  ];
  const filters: ObsFilter[] = [
    {
      name: 'Color',
      kind: 'color_filter',
      enabled: true,
      index: 0,
    },
  ];

  async function loaded(): Promise<[QueryClient, FakeHubConnection]> {
    const [queries, connection] = await started();

    queries.setQueryData(obsSceneItemsKey('c1', 'Main'), items);
    queries.setQueryData(obsSourceFiltersKey('c1', 'Webcam'), filters);

    return [queries, connection];
  }

  const item = (queries: QueryClient, id: number): ObsSceneItem | undefined =>
    queries.getQueryData<ObsSceneItem[]>(obsSceneItemsKey('c1', 'Main'))?.find((entry) => entry.sceneItemId === id);

  it('a visibility change hides only that item', async () => {
    const [queries, connection] = await loaded();

    send(connection, {
      kind: 'sceneItemVisibility',
      sceneName: 'Main',
      sceneItemId: 1,
      enabled: false,
    });

    expect(item(queries, 1)?.enabled).toBe(false);
    expect(item(queries, 2)?.enabled).toBe(true);
  });

  it('a lock change locks only that item', async () => {
    const [queries, connection] = await loaded();

    send(connection, {
      kind: 'sceneItemLock',
      sceneName: 'Main',
      sceneItemId: 2,
      locked: true,
    });

    expect(item(queries, 2)?.locked).toBe(true);
    expect(item(queries, 1)?.locked).toBe(false);
  });

  it('a filter change switches that filter on its source', async () => {
    const [queries, connection] = await loaded();

    send(connection, {
      kind: 'sourceFilter',
      sourceName: 'Webcam',
      filterName: 'Color',
      enabled: false,
    });

    const color = queries.getQueryData<ObsFilter[]>(obsSourceFiltersKey('c1', 'Webcam'))?.[0];

    expect(color?.enabled).toBe(false);
  });
});
