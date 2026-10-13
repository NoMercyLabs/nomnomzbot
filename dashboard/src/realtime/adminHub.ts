// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { HubConnection } from '@microsoft/signalr'
import type { QueryClient } from '@tanstack/vue-query'

import { adminHandlers } from './admin'
import { attachHandlers, buildHub } from './hubConnection'
import { adminHubStatus } from './hubStatus'

// server/src/NomNomzBot.Api/Program.cs: app.MapHub<AdminHub>("/hubs/admin"), policy iam:manage
const hubPath = '/hubs/admin'

let connection: HubConnection | null = null

// A caller without the operator right is refused at the handshake; the hub then stays offline.
export async function startAdminHub(queries: QueryClient): Promise<void> {
  if (connection) return
  const hub: HubConnection = buildHub(hubPath, adminHubStatus)
  attachHandlers(hub, adminHandlers, () => ({ queries, channelId: null }))
  connection = hub
  try {
    await hub.start()
    adminHubStatus.value = 'connected'
  } catch {
    connection = null
    adminHubStatus.value = 'offline'
  }
}

export async function stopAdminHub(): Promise<void> {
  const closing: HubConnection | null = connection
  connection = null
  await closing?.stop()
  adminHubStatus.value = 'offline'
}
