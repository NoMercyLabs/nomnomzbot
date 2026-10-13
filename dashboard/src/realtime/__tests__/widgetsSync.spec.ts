// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { QueryClient } from '@tanstack/vue-query'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { actionRequiredKey } from '@/api/actionRequired'
import { widgetsKey } from '@/api/widgets'

import { FakeHubConnection, FakeHubConnectionBuilder } from './fakeSignalR'

vi.mock('@microsoft/signalr', () =>
  ({
    HubConnectionBuilder: FakeHubConnectionBuilder,
    HubConnection: FakeHubConnection,
  }))

async function started(): Promise<[QueryClient, FakeHubConnection]> {
  const hub = await import('../dashboardHub')
  const queries = new QueryClient()

  await hub.startDashboardHub(queries)
  await hub.watchChannel('c1')

  return [queries, FakeHubConnection.at(FakeHubConnection.instances.length - 1)]
}

beforeEach(() => {
  vi.resetModules()
  FakeHubConnection.instances = []
})

describe('overlay widgets live sync', () => {
  it.each(['created', 'updated', 'deleted'])(
    'a widgets %s change marks the joined channel widget list stale',
    async (action: string) => {
      const [queries, connection] = await started()

      queries.setQueryData(widgetsKey('c1'), [])
      queries.setQueryData(widgetsKey('other'), [])

      connection.receive('ConfigChanged', {
        broadcasterId: 'x',
        domain: 'widgets',
        entityId: 'w1',
        action,
      })

      expect(queries.getQueryState(widgetsKey('c1'))?.isInvalidated).toBe(true)
      expect(queries.getQueryState(widgetsKey('other'))?.isInvalidated).toBe(false)
    },
  )

  it('a notifications change still marks only the action-required inbox stale', async () => {
    const [queries, connection] = await started()

    queries.setQueryData(widgetsKey('c1'), [])
    queries.setQueryData(actionRequiredKey('c1'), [])

    connection.receive('ConfigChanged', {
      broadcasterId: 'x',
      domain: 'notifications',
      entityId: null,
      action: 'updated',
    })

    expect(queries.getQueryState(actionRequiredKey('c1'))?.isInvalidated).toBe(true)
    expect(queries.getQueryState(widgetsKey('c1'))?.isInvalidated).toBe(false)
  })
})
