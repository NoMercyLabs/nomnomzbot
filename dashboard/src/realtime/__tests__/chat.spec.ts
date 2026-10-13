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

import { chatKey, chatLineLimit, type ChatLine } from '@/api/chat'

import type { ChatMessagePayload } from '../payloads'
import { FakeHubConnection, FakeHubConnectionBuilder } from './fakeSignalR'

vi.mock('@microsoft/signalr', () => ({
  HubConnectionBuilder: FakeHubConnectionBuilder,
  HubConnection: FakeHubConnection,
}))

type Hub = typeof import('../dashboardHub')

const line = (id: string, channelId: string, userId: string = 'u1'): ChatMessagePayload => ({
  id,
  channelId,
  userId,
  displayName: userId,
  username: userId,
  message: `message ${id}`,
  fragments: [],
  userType: 'viewer',
  isSubscriber: false,
  isVip: false,
  isModerator: false,
  isBroadcaster: false,
  isCheer: false,
  isCommand: false,
  badges: [],
  bitsAmount: 0,
  color: null,
  messageType: 'text',
  replyToMessageId: null,
  replyParentMessageBody: null,
  replyParentUserName: null,
  timestamp: '2026-10-13T10:00:00Z',
  provider: 'twitch',
})

async function started(): Promise<[Hub, QueryClient, FakeHubConnection]> {
  const hub: Hub = await import('../dashboardHub')
  const queries: QueryClient = new QueryClient()
  await hub.startDashboardHub(queries)
  return [hub, queries, FakeHubConnection.at(FakeHubConnection.instances.length - 1)]
}

const lines = (queries: QueryClient, channelId: string): ChatLine[] | undefined =>
  queries.getQueryData<ChatLine[]>(chatKey(channelId))

beforeEach(() => {
  vi.resetModules()
  FakeHubConnection.instances = []
})

describe('ChatMessage', () => {
  it('lands in the cache of the channel named in the line, not the channel the hub watches', async () => {
    const [hub, queries, connection] = await started()
    await hub.watchChannel('A')
    queries.setQueryData<ChatLine[]>(chatKey('A'), [])
    queries.setQueryData<ChatLine[]>(chatKey('B'), [])

    connection.receive('ChatMessage', line('m1', 'B'))

    expect(lines(queries, 'B')?.map((l) => l.id)).toEqual(['m1'])
    expect(lines(queries, 'A')).toEqual([])
  })

  it('ignores a line whose id is already in the feed', async () => {
    const [, queries, connection] = await started()
    queries.setQueryData<ChatLine[]>(chatKey('A'), [line('m1', 'A')])

    connection.receive('ChatMessage', { ...line('m1', 'A'), message: 'sent again' })
    connection.receive('ChatMessage', line('m2', 'A'))

    expect(lines(queries, 'A')?.map((l) => [l.id, l.message])).toEqual([
      ['m1', 'message m1'],
      ['m2', 'message m2'],
    ])
  })

  it('keeps the newest 500 lines and drops the oldest one when the 501st arrives', async () => {
    const [, queries, connection] = await started()
    const full: ChatLine[] = Array.from({ length: chatLineLimit }, (_, index) =>
      line(`m${index}`, 'A'),
    )
    queries.setQueryData<ChatLine[]>(chatKey('A'), full)

    connection.receive('ChatMessage', line('newest', 'A'))

    const feed: ChatLine[] | undefined = lines(queries, 'A')
    expect(feed).toHaveLength(chatLineLimit)
    expect(feed?.[0]?.id).toBe('m1')
    expect(feed?.at(-1)?.id).toBe('newest')
  })
})

const channelEvent = (type: string, broadcasterId: string, data: unknown): unknown => ({
  type,
  broadcasterId,
  userId: null,
  userDisplayName: null,
  data,
  timestamp: '2026-10-13T10:00:00Z',
})

describe('chat moderation events', () => {
  it('message_deleted marks exactly the line with that id and leaves the others', async () => {
    const [, queries, connection] = await started()
    queries.setQueryData<ChatLine[]>(chatKey('A'), [
      line('m1', 'A'),
      line('m2', 'A'),
      line('m3', 'A'),
    ])
    queries.setQueryData<ChatLine[]>(chatKey('B'), [line('m2', 'B')])

    connection.receive(
      'ChannelEvent',
      channelEvent('message_deleted', 'A', {
        messageId: 'm2',
        deletedByUserId: 'mod',
        targetUserId: 'u1',
        deletedByDisplayName: 'Mod',
      }),
    )

    expect(lines(queries, 'A')?.map((l) => [l.id, l.removed])).toEqual([
      ['m1', undefined],
      ['m2', 'deleted'],
      ['m3', undefined],
    ])
    expect(lines(queries, 'B')?.[0]?.removed).toBeUndefined()
  })

  it('chat_cleared marks every line of that channel and no line of another channel', async () => {
    const [, queries, connection] = await started()
    queries.setQueryData<ChatLine[]>(chatKey('A'), [line('m1', 'A'), line('m2', 'A', 'u2')])
    queries.setQueryData<ChatLine[]>(chatKey('B'), [line('m3', 'B')])

    connection.receive(
      'ChannelEvent',
      channelEvent('chat_cleared', 'A', { clearedByUserId: 'mod' }),
    )

    expect(lines(queries, 'A')?.map((l) => l.removed)).toEqual(['cleared', 'cleared'])
    expect(lines(queries, 'B')?.map((l) => l.removed)).toEqual([undefined])
  })

  it('user_messages_cleared marks the lines of that person in that channel only', async () => {
    const [, queries, connection] = await started()
    queries.setQueryData<ChatLine[]>(chatKey('A'), [line('m1', 'A', 'u1'), line('m2', 'A', 'u2')])
    queries.setQueryData<ChatLine[]>(chatKey('B'), [line('m3', 'B', 'u1')])

    connection.receive(
      'ChannelEvent',
      channelEvent('user_messages_cleared', 'A', {
        targetUserId: 'u1',
        targetUserDisplayName: 'U1',
        targetUserLogin: 'u1',
      }),
    )

    expect(lines(queries, 'A')?.map((l) => [l.userId, l.removed])).toEqual([
      ['u1', 'purged'],
      ['u2', undefined],
    ])
    expect(lines(queries, 'B')?.[0]?.removed).toBeUndefined()
  })

  it.each([['timeout'], ['ban']])(
    'a %s mod action marks that person lines in that channel only',
    async (action: string) => {
      const [, queries, connection] = await started()
      queries.setQueryData<ChatLine[]>(chatKey('A'), [line('m1', 'A', 'u1'), line('m2', 'A', 'u2')])
      queries.setQueryData<ChatLine[]>(chatKey('B'), [line('m3', 'B', 'u1')])

      connection.receive('ModAction', {
        action,
        moderatorId: 'mod',
        targetUserId: 'u1',
        reason: null,
        durationSeconds: 60,
        broadcasterId: 'A',
      })

      expect(lines(queries, 'A')?.map((l) => [l.userId, l.removed])).toEqual([
        ['u1', 'purged'],
        ['u2', undefined],
      ])
      expect(lines(queries, 'B')?.[0]?.removed).toBeUndefined()
    },
  )

  it('an unban mod action leaves the feed alone', async () => {
    const [, queries, connection] = await started()
    queries.setQueryData<ChatLine[]>(chatKey('A'), [line('m1', 'A', 'u1')])

    connection.receive('ModAction', {
      action: 'unban',
      moderatorId: 'mod',
      targetUserId: 'u1',
      reason: null,
      durationSeconds: null,
      broadcasterId: 'A',
    })

    expect(lines(queries, 'A')?.[0]?.removed).toBeUndefined()
  })
})
