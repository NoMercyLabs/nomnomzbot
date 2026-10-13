// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { HubConnection, HubConnectionBuilder } from '@microsoft/signalr';
import type { Ref } from 'vue';

import { activeBot } from '@/api/activeBot';
import { getAccessToken } from '@/api/auth';

import type { HubEventContext, HubEventHandler } from './hubEvent';
import type { HubStatus } from './hubStatus';

// The same base address as the API client. The server reads the token from the query string on /hubs paths.
export function buildHub(path: string, status: Ref<HubStatus>): HubConnection {
  const connection: HubConnection = new HubConnectionBuilder()
    .withUrl(new URL(path, activeBot.value).toString(), { accessTokenFactory: getAccessToken })
    .withAutomaticReconnect()
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
