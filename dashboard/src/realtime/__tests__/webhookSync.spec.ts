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

import { commandsKey } from '@/api/commands';
import { outboundWebhooksKey } from '@/api/webhooks';

import { FakeHubConnection, fakeSignalR } from './fakeSignalR';

vi.mock('@microsoft/signalr', (importOriginal) =>
  fakeSignalR(importOriginal));

beforeEach(() => {
  vi.resetModules();
  FakeHubConnection.instances = [];
});

const failedDelivery = {
  outboundEndpointId: 'e1',
  webhookMessageId: 'm1',
  attempt: 2,
  status: 'Failed',
  responseCode: 500,
  nextRetryAt: null,
};

const autoDisabled = {
  outboundEndpointId: 'e1',
  consecutiveFailureCount: 10,
  reason: 'too many failures',
};

describe('outbound webhooks live sync', () => {
  it.each([
    ['webhook_delivery_failed', failedDelivery],
    ['webhook_delivery_dead_letter', { ...failedDelivery, status: 'DeadLetter' }],
    ['webhook_endpoint_auto_disabled', autoDisabled],
  ])('a %s push marks the endpoint list stale and leaves other lists alone', async (method, payload) => {
    const hub = await import('../dashboardHub');
    const queries = new QueryClient();

    await hub.startDashboardHub(queries);
    await hub.watchChannel('c1');

    const connection = FakeHubConnection.at(FakeHubConnection.instances.length - 1);

    queries.setQueryData(outboundWebhooksKey('c1'), []);
    queries.setQueryData(commandsKey('c1'), []);

    connection.receive('ChannelEvent', {
      type: method,
      broadcasterId: 'c1',
      data: payload,
    });

    expect(queries.getQueryState(outboundWebhooksKey('c1'))?.isInvalidated).toBe(true);
    expect(queries.getQueryState(commandsKey('c1'))?.isInvalidated).toBe(false);
  });
});
