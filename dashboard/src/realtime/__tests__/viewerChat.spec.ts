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

import { viewerChatKey, type ChatLine } from '@/api/chat';

import type { ChatMessagePayload } from '../payloads';
import { FakeHubConnection, fakeSignalR } from './fakeSignalR';

vi.mock('@microsoft/signalr', (importOriginal) =>
  fakeSignalR(importOriginal));

type Hub = typeof import('../dashboardHub');

const line = (id: string, channelId: string): ChatMessagePayload => ({
  id,
  channelId,
  userId: 'u1',
  displayName: 'U1',
  username: 'u1',
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
  timestamp: '2026-10-16T10:00:00Z',
  provider: 'twitch',
});

async function started(): Promise<[Hub, QueryClient, FakeHubConnection]> {
  const hub = await import('../dashboardHub');
  const queries = new QueryClient();

  await hub.startDashboardHub(queries);

  return [hub, queries, FakeHubConnection.at(FakeHubConnection.instances.length - 1)];
}

const lines = (queries: QueryClient): ChatLine[] | undefined =>
  queries.getQueryData<ChatLine[]>(viewerChatKey('A'));

beforeEach(() => {
  vi.resetModules();
  FakeHubConnection.instances = [];
});

describe('the viewer chat feed', () => {
  it('appends a live line to the viewer feed of its channel', async () => {
    const [, queries, connection] = await started();

    queries.setQueryData<ChatLine[]>(viewerChatKey('A'), [line('m1', 'A')]);

    connection.receive('ChatMessage', line('m2', 'A'));

    expect(lines(queries)?.map((l) => l.id)).toEqual(['m1', 'm2']);
  });

  it('marks a deleted line as removed in the viewer feed and leaves the others', async () => {
    const [, queries, connection] = await started();

    queries.setQueryData<ChatLine[]>(viewerChatKey('A'), [line('m1', 'A'), line('m2', 'A')]);

    connection.receive('ChannelEvent', {
      type: 'message_deleted',
      broadcasterId: 'A',
      userId: null,
      userDisplayName: null,
      timestamp: '2026-10-16T10:01:00Z',
      data: {
        messageId: 'm2',
        deletedByUserId: 'mod',
        targetUserId: 'u1',
        deletedByDisplayName: 'Mod',
      },
    });

    expect(lines(queries)?.map((l) => [l.id, l.removed])).toEqual([
      ['m1', undefined],
      ['m2', 'deleted'],
    ]);
  });
});
