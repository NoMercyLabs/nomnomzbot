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

import type { HubConnection } from '@microsoft/signalr';
import type { QueryClient } from '@tanstack/vue-query';

import { adminHandlers } from './admin';
import { attachHandlers, buildHub } from './hubConnection';
import { adminHubStatus } from './hubStatus';

// server/src/NomNomzBot.Api/Program.cs: app.MapHub<AdminHub>("/hubs/admin"), policy iam:manage
const hubPath = '/hubs/admin';

let connection: HubConnection | null = null;

// A caller without the operator right is refused at the handshake; the hub then stays offline.
export async function startAdminHub(queries: QueryClient): Promise<void> {
  if (connection) return;

  const hub = buildHub(hubPath, adminHubStatus);

  attachHandlers(hub, adminHandlers, () => ({ queries, channelId: null }));

  connection = hub;

  try {
    await hub.start();
    adminHubStatus.value = 'connected';
  } catch {
    connection = null;
    adminHubStatus.value = 'offline';
  }
}

export async function stopAdminHub(): Promise<void> {
  const closing = connection;

  connection = null;
  await closing?.stop();
  adminHubStatus.value = 'offline';
}
