// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { chatKey, chatLineLimit, type ChatLine, type ChatLineMark } from '@/api/chat';

import type { HubEventContext, HubEventHandler } from './hubEvent';
import type {
  ChannelEventPayload,
  ChatMessagePayload,
  MessageDeletedPayload,
  ModActionPayload,
  UserMessagesClearedPayload,
} from './payloads';

// A removed line stays in the feed, marked, so the page decides whether to hide or strike it.
function markLines(
  queries: HubEventContext['queries'],
  channelId: string,
  mark: ChatLineMark,
  applies: (line: ChatLine) => boolean,
): void {
  queries.setQueryData<ChatLine[]>(chatKey(channelId), (feed) =>
    feed?.map((line) =>
      applies(line)
        ? { ...line, ...mark }
        : line),
  );
}

// A timeout and a ban say what happened; Twitch also sends a purge for them, which must not wipe that.
function modActionMark(action: ModActionPayload): ChatLineMark | null {
  if (action.action === 'timeout') {
    return { removed: 'timeout', timeoutSeconds: action.durationSeconds ?? undefined };
  }

  if (action.action === 'ban') {
    return { removed: 'banned' };
  }

  return null;
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
      );
    },
  },
  {
    event: 'ChannelEvent',
    handle: ({ queries }, event) => {
      if (event.type === 'message_deleted') {
        const { messageId } = event.data as MessageDeletedPayload;

        markLines(
          queries,
          event.broadcasterId,
          { removed: 'deleted' },
          (line) => line.id === messageId,
        );
      }

      if (event.type === 'chat_cleared') {
        markLines(queries, event.broadcasterId, { removed: 'cleared' }, () =>
          true);
      }

      if (event.type === 'user_messages_cleared') {
        const { targetUserId } = event.data as UserMessagesClearedPayload;
        const isUnmarkedLineOf = (line: ChatLine): boolean =>
          line.userId === targetUserId && line.removed === undefined;

        markLines(queries, event.broadcasterId, { removed: 'purged' }, isUnmarkedLineOf);
      }
    },
  },
  {
    event: 'ModAction',
    handle: ({ queries }, action) => {
      const mark = modActionMark(action);

      if (!mark) return;

      markLines(queries, action.broadcasterId, mark, (line) =>
        line.userId === action.targetUserId);
    },
  },
];
