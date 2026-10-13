// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { HubConnection, HubConnectionBuilder, LogLevel, type IRetryPolicy } from '@microsoft/signalr';
import type { Ref } from 'vue';

import { activeBot } from '@/api/activeBot';
import { getAccessToken } from '@/api/auth';

import type { HubEventContext, HubEventHandler } from './hubEvent';
import type { HubStatus } from './hubStatus';

// Retry at once, then after 2 s, 10 s and every 30 s from there on.
const retryDelays = [0, 2_000, 10_000, 30_000] as const;

export function retryDelay(attempt: number): number {
  return retryDelays[Math.min(attempt, retryDelays.length - 1)] ?? 30_000;
}

// The built-in policy gives up after four tries; this one never returns null, so a long outage still recovers.
export const reconnectPolicy = {
  nextRetryDelayInMilliseconds: ({ previousRetryCount }) => retryDelay(previousRetryCount),
} as const satisfies IRetryPolicy;

// The same base address as the API client. The server reads the token from the query string on /hubs paths;
// the client logs that URL at Information level, so logging stays at Warning to keep the token out of the console.
export function buildHub(path: string, status: Ref<HubStatus>): HubConnection {
  const connection: HubConnection = new HubConnectionBuilder()
    .withUrl(new URL(path, activeBot.value).toString(), { accessTokenFactory: getAccessToken })
    .configureLogging(LogLevel.Warning)
    .withAutomaticReconnect(reconnectPolicy)
    .build();

  connection.onreconnecting(() => {
    status.value = 'reconnecting';
  });

  connection.onreconnected(() => {
    status.value = 'connected';
  });

  connection.onclose(() => {
    status.value = 'offline';
  });

  return connection;
}

export function attachHandlers(
  connection: HubConnection,
  handlers: readonly HubEventHandler<never>[],
  context: () => HubEventContext,
): void {
  for (const { event, handle } of handlers) {
    connection.on(event, (payload: never) => {
      handle(context(), payload);
    });
  }
}
