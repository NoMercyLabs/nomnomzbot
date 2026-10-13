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
import { chatHandlers } from './chat'
import { communityHandlers } from './community'
import { attachHandlers, buildHub } from './hubConnection'
import { dashboardHubStatus, dashboardJoinErrors } from './hubStatus'
import type { JoinChannelResult } from './payloads'

// server/src/NomNomzBot.Api/Program.cs: app.MapHub<DashboardHub>("/hubs/dashboard")
const hubPath = '/hubs/dashboard'

let connection: HubConnection | null = null
let joinedChannel: string | null = null
// The channels watched with their own event classes (the chat page). Separate from joinedChannel, which the other pages use.
const watched = new Map()

export type ChannelWatch = { id: string; classes: string[] }

async function join(channelId: string): Promise<void> {
  await connection?.invoke('JoinChannel', channelId)
}

function recordJoinError(channelId: string, message: string): void {
  dashboardJoinErrors.value = [
    ...dashboardJoinErrors.value.filter((error) => error.channelId !== channelId),
    { channelId, message },
  ]
}

async function joinWithClasses(channelId: string, classes: string[]): Promise<void> {
  const result = await connection?.invoke<JoinChannelResult>(
    'JoinChannelClasses',
    channelId,
    classes,
  )

  dashboardJoinErrors.value = dashboardJoinErrors.value.filter(
    (error) => error.channelId !== channelId,
  )
  if (result && !result.success) recordJoinError(channelId, result.error ?? 'The join was refused.')
}

async function joinWatched(): Promise<void> {
  await Promise.all([...watched].map(([channelId, classes]) => joinWithClasses(channelId, classes)))
}

// Groups belong to a connection id, so a new connection after a drop joins the channel again.
export async function startDashboardHub(queries: QueryClient): Promise<void> {
  if (connection) return
  const hub = buildHub(hubPath, dashboardHubStatus)

  attachHandlers(hub, [...channelHandlers, ...communityHandlers, ...chatHandlers], () => ({
    queries,
    channelId: joinedChannel,
  }))
  hub.onreconnected(() => {
    if (joinedChannel) void join(joinedChannel)
    void joinWatched()
  })
  connection = hub
  await hub.start()
  dashboardHubStatus.value = 'connected'
  if (joinedChannel) await join(joinedChannel)
  await joinWatched()
}

// Makes the watched set equal to `channels`: new ids are joined with their classes, removed ids are left.
// A channel whose classes changed is joined again, which replaces its class set on the server.
export async function watchChannels(channels: ChannelWatch[]): Promise<void> {
  const next: Map<string, string[]> = new Map(
    channels.map((channel) => [channel.id, channel.classes]),
  )
  const removed = [...watched.keys()].filter((id) => !next.has(id))
  const changed: ChannelWatch[] = channels.filter(
    (channel) => watched.get(channel.id)?.join(',') !== channel.classes.join(','),
  )

  watched.clear()
  for (const [id, classes] of next) watched.set(id, classes)
  if (!connection) return

  for (const id of removed) {
    // The frame's own join of this channel stays; the page only drops its class set.
    if (id === joinedChannel) continue
    await connection.invoke('LeaveChannel', id)
    dashboardJoinErrors.value = dashboardJoinErrors.value.filter((error) => error.channelId !== id)
  }

  for (const channel of changed) await joinWithClasses(channel.id, channel.classes)
}

export async function watchChannel(channelId: string | null): Promise<void> {
  if (channelId === joinedChannel) return
  const previous = joinedChannel

  joinedChannel = channelId
  if (!connection) return
  if (previous) await connection.invoke('LeaveChannel', previous)
  if (channelId) await join(channelId)
}

export async function stopDashboardHub(): Promise<void> {
  const closing = connection

  connection = null
  joinedChannel = null
  watched.clear()
  dashboardJoinErrors.value = []
  await closing?.stop()
  dashboardHubStatus.value = 'offline'
}
