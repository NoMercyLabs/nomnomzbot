// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { chatKey, chatLineLimit, type ChatLine, type ChatLineRemoval } from '@/api/chat'

import type { HubEventContext, HubEventHandler } from './hubEvent'
import type {
  ChannelEventPayload,
  ChatMessagePayload,
  MessageDeletedPayload,
  ModActionPayload,
  UserMessagesClearedPayload,
} from './payloads'

// A removed line stays in the feed, marked, so the page decides whether to hide or strike it.
function markLines(
  queries: HubEventContext['queries'],
  channelId: string,
  removal: ChatLineRemoval,
  applies: (line: ChatLine) => boolean,
): void {
  queries.setQueryData<ChatLine[]>(chatKey(channelId), (feed) =>
    feed?.map((line) => (applies(line) ? { ...line, removed: removal } : line)),
  )
}

// One connection watches many channels, so every handler files by the channel the payload names, not the joined one.
// A channel whose feed was never loaded has no cache; the page seeds it from the history call.
export const chatHandlers: [
  HubEventHandler<ChatMessagePayload>,
  HubEventHandler<ChannelEventPayload>,
  HubEventHandler<ModActionPayload>,
] = [
  {
    event: 'ChatMessage',
    handle: ({ queries }, message) => {
      queries.setQueryData<ChatLine[]>(chatKey(message.channelId), (feed) =>
        feed && !feed.some((existing) => existing.id === message.id)
          ? [...feed, message].slice(-chatLineLimit)
          : feed,
      )
    },
  },
  {
    event: 'ChannelEvent',
    handle: ({ queries }, event) => {
      if (event.type === 'message_deleted') {
        const { messageId } = event.data as MessageDeletedPayload

        markLines(queries, event.broadcasterId, 'deleted', (line) => line.id === messageId)
      }

      if (event.type === 'chat_cleared')
        markLines(queries, event.broadcasterId, 'cleared', () => true)

      if (event.type === 'user_messages_cleared') {
        const { targetUserId } = event.data as UserMessagesClearedPayload

        markLines(queries, event.broadcasterId, 'purged', (line) => line.userId === targetUserId)
      }
    },
  },
  {
    event: 'ModAction',
    handle: ({ queries }, action) => {
      if (action.action === 'timeout' || action.action === 'ban')
        markLines(
          queries,
          action.broadcasterId,
          'purged',
          (line) => line.userId === action.targetUserId,
        )
    },
  },
]
