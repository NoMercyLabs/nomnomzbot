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

import { QueryClient } from '@tanstack/vue-query';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { commandsKey } from '@/api/commands';
import { ttsConfigKey } from '@/api/tts';

import { FakeHubConnection, fakeSignalR } from './fakeSignalR';

vi.mock('@microsoft/signalr', (importOriginal) =>
  fakeSignalR(importOriginal));

async function started(): Promise<[QueryClient, FakeHubConnection]> {
  const hub = await import('../dashboardHub');
  const queries = new QueryClient();

  await hub.startDashboardHub(queries);
  await hub.watchChannel('c1');

  return [queries, FakeHubConnection.at(FakeHubConnection.instances.length - 1)];
}

beforeEach(() => {
  vi.resetModules();
  FakeHubConnection.instances = [];
});

describe('text to speech live sync', () => {
  it('a tts-config change marks the tts config stale and leaves the command list alone', async () => {
    const [queries, connection] = await started();

    queries.setQueryData(ttsConfigKey('c1'), { defaultVoiceId: 'a' });
    queries.setQueryData(commandsKey('c1'), []);

    connection.receive('ConfigChanged', {
      broadcasterId: 'x',
      domain: 'tts-config',
      entityId: null,
      action: 'updated',
    });

    expect(queries.getQueryState(ttsConfigKey('c1'))?.isInvalidated).toBe(true);
    expect(queries.getQueryState(commandsKey('c1'))?.isInvalidated).toBe(false);
  });

  it('a change in another domain leaves the tts config alone', async () => {
    const [queries, connection] = await started();

    queries.setQueryData(ttsConfigKey('c1'), { defaultVoiceId: 'a' });

    connection.receive('ConfigChanged', {
      broadcasterId: 'x',
      domain: 'commands',
      entityId: null,
      action: 'updated',
    });

    expect(queries.getQueryState(ttsConfigKey('c1'))?.isInvalidated).toBe(false);
  });
});
