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
import { builtinsKey, commandsKey, type CommandListItem } from '@/api/commands'

import { FakeHubConnection, FakeHubConnectionBuilder } from './fakeSignalR'

vi.mock('@microsoft/signalr', () => ({
  HubConnectionBuilder: FakeHubConnectionBuilder,
  HubConnection: FakeHubConnection,
}))

type Hub = typeof import('../dashboardHub')

async function started(): Promise<[Hub, QueryClient, FakeHubConnection]> {
  const hub = await import('../dashboardHub')
  const queries = new QueryClient()

  await hub.startDashboardHub(queries)
  await hub.watchChannel('c1')

  return [hub, queries, FakeHubConnection.at(FakeHubConnection.instances.length - 1)]
}

const row = (name: string, useCount: number): CommandListItem =>
  ({ id: name, name, useCount, isEnabled: true, tier: 'template' }) as unknown as CommandListItem

beforeEach(() => {
  vi.resetModules()
  FakeHubConnection.instances = []
})

describe('commands live sync', () => {
  it('a commands config change marks the command list stale and leaves the other lists alone', async () => {
    const [, queries, connection] = await started()

    queries.setQueryData(commandsKey('c1'), [row('!a', 1)])
    queries.setQueryData(builtinsKey('c1'), [])
    queries.setQueryData(actionRequiredKey('c1'), [])

    connection.receive('ConfigChanged', {
      broadcasterId: 'x',
      domain: 'commands',
      entityId: 'id',
      action: 'updated',
    })

    expect(queries.getQueryState(commandsKey('c1'))?.isInvalidated).toBe(true)
    expect(queries.getQueryState(builtinsKey('c1'))?.isInvalidated).toBe(false)
    expect(queries.getQueryState(actionRequiredKey('c1'))?.isInvalidated).toBe(false)
  })

  it('a builtins config change marks the built-in list stale only', async () => {
    const [, queries, connection] = await started()

    queries.setQueryData(commandsKey('c1'), [row('!a', 1)])
    queries.setQueryData(builtinsKey('c1'), [])

    connection.receive('ConfigChanged', {
      broadcasterId: 'x',
      domain: 'builtins',
      entityId: 'sr',
      action: 'updated',
    })

    expect(queries.getQueryState(builtinsKey('c1'))?.isInvalidated).toBe(true)
    expect(queries.getQueryState(commandsKey('c1'))?.isInvalidated).toBe(false)
  })

  it('a notifications change still marks the action-required inbox stale', async () => {
    const [, queries, connection] = await started()

    queries.setQueryData(actionRequiredKey('c1'), [])
    queries.setQueryData(commandsKey('c1'), [])

    connection.receive('ConfigChanged', {
      broadcasterId: 'x',
      domain: 'notifications',
      entityId: null,
      action: 'updated',
    })

    expect(queries.getQueryState(actionRequiredKey('c1'))?.isInvalidated).toBe(true)
    expect(queries.getQueryState(commandsKey('c1'))?.isInvalidated).toBe(false)
  })

  it('a run of a command adds one use to that row only, with or without the leading !', async () => {
    const [, queries, connection] = await started()

    queries.setQueryData(commandsKey('c1'), [row('!hello', 4), row('!other', 9)])

    connection.receive('CommandExecuted', {
      broadcasterId: 'x',
      commandName: 'hello',
      triggeredByUserId: 'u',
      succeeded: true,
      timestamp: '',
    })

    connection.receive('CommandExecuted', {
      broadcasterId: 'x',
      commandName: '!hello',
      triggeredByUserId: 'u',
      succeeded: true,
      timestamp: '',
    })

    const rows: CommandListItem[] | undefined = queries.getQueryData(commandsKey('c1'))

    expect(rows?.map((r: CommandListItem) => [r.name, Number(r.useCount)])).toEqual([
      ['!hello', 6],
      ['!other', 9],
    ])
  })

  it('a failed run adds no use', async () => {
    const [, queries, connection] = await started()

    queries.setQueryData(commandsKey('c1'), [row('!hello', 4)])

    connection.receive('CommandExecuted', {
      broadcasterId: 'x',
      commandName: '!hello',
      triggeredByUserId: 'u',
      succeeded: false,
      timestamp: '',
    })

    expect(queries.getQueryData<CommandListItem[]>(commandsKey('c1'))?.[0]?.useCount).toBe(4)
  })

  it('a run before the list has loaded creates nothing', async () => {
    const [, queries, connection] = await started()

    connection.receive('CommandExecuted', {
      broadcasterId: 'x',
      commandName: '!hello',
      triggeredByUserId: 'u',
      succeeded: true,
      timestamp: '',
    })

    expect(queries.getQueryData(commandsKey('c1'))).toBeUndefined()
  })
})
