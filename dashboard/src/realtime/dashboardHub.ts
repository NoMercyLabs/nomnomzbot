// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { HubConnection } from '@microsoft/signalr';
import type { QueryClient } from '@tanstack/vue-query';

import { promiseTimeout } from '@vueuse/core';

import { merge } from '@/lib/array';

import { channelHandlers } from './channels';
import { chatHandlers } from './chat';
import { communityHandlers } from './community';
import { attachHandlers, buildHub, retryDelay } from './hubConnection';
import { dashboardHubStatus, dashboardJoinErrors } from './hubStatus';
import { liveHandlers } from './liveSync';
import type { JoinChannelResult } from './payloads';

// server/src/NomNomzBot.Api/Program.cs: app.MapHub<DashboardHub>("/hubs/dashboard")
const hubPath = '/hubs/dashboard';

let connection: HubConnection | null = null;
let joinedChannel: string | null = null;
// The channels watched with their own event classes (the chat page). Separate from joinedChannel, which the other pages use.
const watched = new Map();

export type ChannelWatch = { id: string; classes: string[] };

function recordJoinError(channelId: string, message: string): void {
  dashboardJoinErrors.value = [
    ...dashboardJoinErrors.value.filter((error) => error.channelId !== channelId),
    { channelId, message },
  ];
}

// A refused join answers success false instead of throwing, so the answer is the only sign of it.
function settleJoin(channelId: string, result: JoinChannelResult | undefined): void {
  dashboardJoinErrors.value = dashboardJoinErrors.value.filter(
    (error) => error.channelId !== channelId,
  );

  if (result && !result.success) {
    recordJoinError(channelId, result.error ?? 'The join was refused.');
  }
}

// True only while the transport is up. A call while it starts, reconnects or closes would throw, so it waits:
// the start and the reconnect both join the current channel and the watched set again.
let linked = false;

async function send<T>(method: string, ...args: unknown[]): Promise<T | undefined> {
  if (!connection || !linked) return;

  try {
    return await connection.invoke<T>(method, ...args);
  } catch {
    // The link dropped during the call; the reconnect joins again.
    return undefined;
  }
}

async function join(channelId: string): Promise<void> {
  const result = await send<JoinChannelResult>('JoinChannel', channelId);

  settleJoin(channelId, result);
}

async function joinWithClasses(channelId: string, classes: string[]): Promise<void> {
  const result = await send<JoinChannelResult>('JoinChannelClasses', channelId, classes);

  settleJoin(channelId, result);
}

async function joinWatched(): Promise<void> {
  await Promise.all(
    [...watched].map(([channelId, classes]) => joinWithClasses(channelId, classes)),
  );
}

// Tries to start until it works. Stopping the hub (which clears `connection`) ends the loop; the answer is false then.
// The first wait is the second delay step because the first attempt just failed.
async function startWithRetry(hub: HubConnection): Promise<boolean> {
  for (let attempt = 1; connection === hub; attempt++) {
    try {
      await hub.start();

      return connection === hub;
    } catch {
      dashboardHubStatus.value = 'reconnecting';
      await promiseTimeout(retryDelay(attempt));
    }
  }

  return false;
}

// Groups belong to a connection id, so a new connection after a drop joins the channel again.
// Whatever changed while the link was down is unknown, so every active query loads again.
function afterReconnect(queries: QueryClient): void {
  void queries.invalidateQueries();

  if (joinedChannel) {
    void join(joinedChannel);
  }

  void joinWatched();
}

export async function startDashboardHub(queries: QueryClient): Promise<void> {
  if (connection) return;

  const hub = buildHub(hubPath, dashboardHubStatus);

  attachHandlers(
    hub,
    merge(channelHandlers, communityHandlers, chatHandlers, liveHandlers),
    () => ({
      queries,
      channelId: joinedChannel,
    }),
  );

  hub.onreconnecting(() => {
    linked = false;
  });

  hub.onclose(() => {
    linked = false;
  });

  hub.onreconnected(() => {
    linked = true;
    afterReconnect(queries);
  });

  connection = hub;

  if (!(await startWithRetry(hub))) return;

  linked = true;
  dashboardHubStatus.value = 'connected';

  if (joinedChannel) {
    await join(joinedChannel);
  }

  await joinWatched();
}

// Makes the watched set equal to `channels`: new ids are joined with their classes, removed ids are left.
// A channel whose classes changed is joined again, which replaces its class set on the server.
export async function watchChannels(channels: ChannelWatch[]): Promise<void> {
  const next = new Map<string, string[]>(
    channels.map((channel) => [channel.id, channel.classes]),
  );
  const removed = [...watched.keys()].filter((id) => !next.has(id));
  const changed: ChannelWatch[] = channels.filter(
    (channel) => watched.get(channel.id)?.join(',') !== channel.classes.join(','),
  );

  watched.clear();

  for (const [id, classes] of next) {
    watched.set(id, classes);
  }

  if (!linked) return;

  for (const id of removed) {
    // The frame's own join of this channel stays; the page only drops its class set.
    if (id === joinedChannel) continue;

    await send('LeaveChannel', id);
    dashboardJoinErrors.value = dashboardJoinErrors.value.filter((error) => error.channelId !== id);
  }

  for (const channel of changed) {
    await joinWithClasses(channel.id, channel.classes);
  }
}

export async function watchChannel(channelId: string | null): Promise<void> {
  if (channelId === joinedChannel) return;

  const previous = joinedChannel;

  joinedChannel = channelId;

  if (!linked) return;

  if (previous) {
    await send('LeaveChannel', previous);
  }

  if (channelId) {
    await join(channelId);
  }
}

export async function stopDashboardHub(): Promise<void> {
  const closing = connection;

  connection = null;
  linked = false;
  joinedChannel = null;
  watched.clear();
  dashboardJoinErrors.value = [];
  await closing?.stop();
  dashboardHubStatus.value = 'offline';
}
