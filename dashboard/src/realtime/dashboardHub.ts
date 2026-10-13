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

import { channelHandlers } from './channels'
import { communityHandlers } from './community'
import { attachHandlers, buildHub } from './hubConnection'
import { dashboardHubStatus } from './hubStatus'

// server/src/NomNomzBot.Api/Program.cs: app.MapHub<DashboardHub>("/hubs/dashboard")
const hubPath = '/hubs/dashboard'

let connection: HubConnection | null = null
let joinedChannel: string | null = null

async function join(channelId: string): Promise<void> {
  await connection?.invoke('JoinChannel', channelId)
}

// Groups belong to a connection id, so a new connection after a drop joins the channel again.
export async function startDashboardHub(queries: QueryClient): Promise<void> {
  if (connection) return
  const hub: HubConnection = buildHub(hubPath, dashboardHubStatus)
  attachHandlers(hub, [...channelHandlers, ...communityHandlers],() => ({ queries, channelId: joinedChannel }))
  hub.onreconnected(() => {
    if (joinedChannel) void join(joinedChannel)
  })
  connection = hub
  await hub.start()
  dashboardHubStatus.value = 'connected'
  if (joinedChannel) await join(joinedChannel)
}

export async function watchChannel(channelId: string | null): Promise<void> {
  if (channelId === joinedChannel) return
  const previous: string | null = joinedChannel
  joinedChannel = channelId
  if (!connection) return
  if (previous) await connection.invoke('LeaveChannel', previous)
  if (channelId) await join(channelId)
}

export async function stopDashboardHub(): Promise<void> {
  const closing: HubConnection | null = connection
  connection = null
  joinedChannel = null
  await closing?.stop()
  dashboardHubStatus.value = 'offline'
}
