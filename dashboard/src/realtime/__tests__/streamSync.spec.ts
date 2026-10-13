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

import { streamHistoryKey, streamInfoKey, type StreamInfo } from '@/api/stream'

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

const info: StreamInfo = {
  title: 'Old title',
  gameName: 'Old game',
  tags: ['a'],
  isLive: false,
  viewerCount: 0,
  startedAt: null,
  language: 'en',
}

beforeEach(() => {
  vi.resetModules()
  FakeHubConnection.instances = []
})

describe('stream live sync', () => {
  it('going live writes the live state into the stream info and marks the history stale', async () => {
    const [queries, connection] = await started()

    queries.setQueryData(streamInfoKey('c1'), info)
    queries.setQueryData(streamHistoryKey('c1'), [])

    connection.receive('StreamStatusChanged', {
      isLive: true,
      streamId: 's9',
      title: 'New title',
      gameName: 'New game',
      startedAt: '2026-10-13T10:00:00Z',
    })

    expect(queries.getQueryData<StreamInfo>(streamInfoKey('c1'))).toMatchObject({
      isLive: true,
      title: 'New title',
      gameName: 'New game',
      startedAt: '2026-10-13T10:00:00Z',
      tags: ['a'],
    })
    expect(queries.getQueryState(streamHistoryKey('c1'))?.isInvalidated).toBe(true)
  })

  it('going offline keeps the title when the payload carries none', async () => {
    const [queries, connection] = await started()

    queries.setQueryData(streamInfoKey('c1'), {
      ...info,
      isLive: true,
      startedAt: '2026-10-13T10:00:00Z',
    })

    connection.receive('StreamStatusChanged', {
      isLive: false,
      streamId: null,
      title: null,
      gameName: null,
      startedAt: null,
    })

    expect(queries.getQueryData<StreamInfo>(streamInfoKey('c1'))).toMatchObject({
      isLive: false,
      title: 'Old title',
      gameName: 'Old game',
    })
  })

  it('a title change from Twitch updates the title and category only', async () => {
    const [queries, connection] = await started()

    queries.setQueryData(streamInfoKey('c1'), info)

    connection.receive('StreamInfoChanged', {
      broadcasterId: 'x',
      broadcasterDisplayName: 'Stoney_Eagle',
      title: 'From Twitch',
      gameName: 'Just Chatting',
    })

    expect(queries.getQueryData<StreamInfo>(streamInfoKey('c1'))).toMatchObject({
      title: 'From Twitch',
      gameName: 'Just Chatting',
      tags: ['a'],
      language: 'en',
    })
  })

  it('a title change creates no cache entry when the page never loaded the stream info', async () => {
    const [queries, connection] = await started()

    connection.receive('StreamInfoChanged', {
      broadcasterId: 'x',
      broadcasterDisplayName: 'Stoney_Eagle',
      title: 'From Twitch',
      gameName: 'Just Chatting',
    })

    expect(queries.getQueryData(streamInfoKey('c1'))).toBeUndefined()
  })
})
