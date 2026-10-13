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
import type { ChannelSummary } from '@/api/channels'
import { myChannelsQuery } from '@/api/channels'
import { shieldKey } from '@/api/shield'

import { FakeHubConnection, FakeHubConnectionBuilder } from './fakeSignalR'

vi.mock('@microsoft/signalr', () =>
  ({
    HubConnectionBuilder: FakeHubConnectionBuilder,
    HubConnection: FakeHubConnection,
  }))

type Hub = typeof import('../dashboardHub')
type Status = (typeof import('../hubStatus'))['dashboardHubStatus']

// Modules are loaded again for each test, so the status ref comes from the same load as the hub.
let dashboardHubStatus: Status

const row = (id: string, isLive: boolean): ChannelSummary => ({
  id,
  login: id,
  displayName: id,
  profileImageUrl: null,
  isLive,
  role: 'broadcaster',
  viewerCount: null,
  provider: 'twitch',
})

async function started(): Promise<[Hub, QueryClient, FakeHubConnection]> {
  const hub = await import('../dashboardHub')

  dashboardHubStatus = (await import('../hubStatus')).dashboardHubStatus
  const queries = new QueryClient()

  await hub.startDashboardHub(queries)

  return [hub, queries, FakeHubConnection.at(FakeHubConnection.instances.length - 1)]
}

beforeEach(() => {
  vi.resetModules()
  FakeHubConnection.instances = []
})

describe('dashboard hub events', () => {
  it('a stream status push sets isLive on the joined channel row only', async () => {
    const [hub, queries, connection] = await started()

    queries.setQueryData(myChannelsQuery.queryKey, [row('c1', false), row('c2', false)])
    await hub.watchChannel('c1')

    connection.receive('StreamStatusChanged', {
      isLive: true,
      streamId: 's',
      title: 't',
      gameName: 'g',
      startedAt: null,
    })

    const rows: ChannelSummary[] | undefined = queries.getQueryData(myChannelsQuery.queryKey)

    expect(rows?.map((r) => [r.id, r.isLive])).toEqual([
      ['c1', true],
      ['c2', false],
    ])
  })

  it('shield mode begin and end write the boolean the shield query reads', async () => {
    const [hub, queries, connection] = await started()

    await hub.watchChannel('c1')

    connection.receive('ChannelEvent', {
      type: 'shield_mode_begin',
      broadcasterId: 'x',
      userId: null,
      userDisplayName: null,
      data: {},
      timestamp: '',
    })
    expect(queries.getQueryData(shieldKey('c1'))).toBe(true)
    connection.receive('ChannelEvent', {
      type: 'shield_mode_end',
      broadcasterId: 'x',
      userId: null,
      userDisplayName: null,
      data: {},
      timestamp: '',
    })
    expect(queries.getQueryData(shieldKey('c1'))).toBe(false)
  })

  it('a notifications config change marks the action-required inbox stale', async () => {
    const [hub, queries, connection] = await started()

    await hub.watchChannel('c1')
    queries.setQueryData(actionRequiredKey('c1'), [])

    connection.receive('ConfigChanged', {
      broadcasterId: 'x',
      domain: 'notifications',
      entityId: null,
      action: 'updated',
    })

    expect(queries.getQueryState(actionRequiredKey('c1'))?.isInvalidated).toBe(true)
  })
})

describe('dashboard hub channel groups', () => {
  it('a channel change leaves the old group and joins the new one', async () => {
    const [hub, , connection] = await started()

    await hub.watchChannel('c1')
    await hub.watchChannel('c2')

    expect(connection.invocations).toEqual([
      ['JoinChannel', 'c1'],
      ['LeaveChannel', 'c1'],
      ['JoinChannel', 'c2'],
    ])
  })

  it('joins the channel again after the connection reconnects', async () => {
    const [hub, , connection] = await started()

    await hub.watchChannel('c1')
    connection.lifecycle.reconnecting()
    expect(dashboardHubStatus.value).toBe('reconnecting')
    connection.lifecycle.reconnected()

    expect(dashboardHubStatus.value).toBe('connected')
    expect(connection.invocations.filter(([method]) => method === 'JoinChannel')).toHaveLength(2)
  })

  it('joins every watched channel with its classes again after a reconnect', async () => {
    const [hub, , connection] = await started()

    await hub.watchChannels([
      { id: 'c1', classes: ['chat', 'moderation'] },
      { id: 'c2', classes: ['chat'] },
    ])
    connection.invocations.length = 0
    connection.lifecycle.reconnected()

    expect(connection.invocations).toEqual([
      ['JoinChannelClasses', 'c1', ['chat', 'moderation']],
      ['JoinChannelClasses', 'c2', ['chat']],
    ])
  })

  it('dropping one channel from the watched set leaves only that channel', async () => {
    const [hub, , connection] = await started()

    await hub.watchChannels([
      { id: 'c1', classes: ['chat'] },
      { id: 'c2', classes: ['chat'] },
    ])
    connection.invocations.length = 0
    await hub.watchChannels([{ id: 'c2', classes: ['chat'] }])

    expect(connection.invocations).toEqual([['LeaveChannel', 'c1']])
  })

  it('dropping a watched channel that the frame also watches does not leave it', async () => {
    const [hub, , connection] = await started()

    await hub.watchChannel('c1')
    await hub.watchChannels([{ id: 'c1', classes: ['chat'] }])
    connection.invocations.length = 0
    await hub.watchChannels([])

    expect(connection.invocations).toEqual([])
  })

  it('reports a refused join with the server message and clears it when the channel is left', async () => {
    const [hub, , connection] = await started()
    const { dashboardJoinErrors } = await import('../hubStatus')

    connection.answer = (method, args) =>
      method === 'JoinChannelClasses' && args[0] === 'c2'
        ? { success: false, error: 'Access denied' }
        : { success: true }

    await hub.watchChannels([
      { id: 'c1', classes: ['chat'] },
      { id: 'c2', classes: ['chat'] },
    ])
    expect(dashboardJoinErrors.value).toEqual([{ channelId: 'c2', message: 'Access denied' }])

    await hub.watchChannels([{ id: 'c1', classes: ['chat'] }])
    expect(dashboardJoinErrors.value).toEqual([])
  })

  it('stopping closes the connection and reports offline', async () => {
    const [hub, , connection] = await started()

    expect(dashboardHubStatus.value).toBe('connected')
    await hub.stopDashboardHub()

    expect(connection.stopped).toBe(true)
    expect(dashboardHubStatus.value).toBe('offline')
  })
})
