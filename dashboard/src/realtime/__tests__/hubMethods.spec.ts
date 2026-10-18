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
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { FakeHubConnection, fakeSignalR } from './fakeSignalR';

vi.mock('@microsoft/signalr', (importOriginal) =>
  fakeSignalR(importOriginal));

beforeEach(() => {
  vi.resetModules();
  FakeHubConnection.instances = [];
});

const clientsFolder = join(__dirname, '../../../../server/src/NomNomzBot.Api/Hubs/Clients');
const interfaceMethod = /^\s*Task\s+(\w+)\s*\(/gm;

// The hub methods the server can call on a client are the methods of its typed client interface.
function serverMethods(file: string): string[] {
  const text = readFileSync(join(clientsFolder, file), 'utf8');

  return [...text.matchAll(interfaceMethod)].map((match) => match[1] ?? '');
}

describe('dashboard listeners match what the server sends', () => {
  it('reads the server interfaces', () => {
    expect(serverMethods('IDashboardClient.cs')).toContain('ChannelEvent');
    expect(serverMethods('IAdminClient.cs')).toContain('ReceiveSystemStatus');
  });

  // The dashboard hub carries one generic ChannelEvent with a type; a listener named after a type never fires.
  it('every dashboard hub listener is a method of IDashboardClient', async () => {
    const { startDashboardHub } = await import('../dashboardHub');

    await startDashboardHub(new QueryClient());

    const listened = [...FakeHubConnection.at(0).handlers.keys()];
    const unknown = listened.filter((name) => !serverMethods('IDashboardClient.cs').includes(name));

    expect(listened.length).toBeGreaterThan(0);
    expect(unknown).toEqual([]);
  });

  it('every admin hub listener is a method of IAdminClient', async () => {
    const { startAdminHub } = await import('../adminHub');

    await startAdminHub(new QueryClient());

    const listened = [...FakeHubConnection.at(0).handlers.keys()];
    const unknown = listened.filter((name) => !serverMethods('IAdminClient.cs').includes(name));

    expect(listened.length).toBeGreaterThan(0);
    expect(unknown).toEqual([]);
  });
});
