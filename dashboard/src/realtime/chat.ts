// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { chatKey, chatLineLimit, viewerChatKey, type ChatLine, type ChatLineMark } from '@/api/chat';
import type { KeyParts } from '@/lib/array';

import type { HubEventContext, HubEventHandler } from './hubEvent';
import type {
  ChannelEventPayload,
  ChatMessagePayload,
  MessageDeletedPayload,
  ModActionPayload,
  RedemptionActivityPayload,
  RewardRedeemedPayload,
  UserMessagesClearedPayload,
} from './payloads';
import { activityLine, rewardLine } from './redemptionLines';

// A removed line stays in the feed, marked, so the page decides whether to hide or strike it.
function markLines(
  queries: HubEventContext['queries'],
  channelId: string,
  mark: ChatLineMark,
  applies: (line: ChatLine) => boolean,
): void {
  for (const key of feedKeys(channelId)) {
    queries.setQueryData<ChatLine[]>(key, (feed) =>
      feed?.map((line) =>
        applies(line)
          ? { ...line, ...mark }
          : line));
  }
}

// One channel has two feeds: the operator chat and the viewer chat. A push updates whichever is loaded.
function feedKeys(channelId: string): KeyParts[] {
  return [chatKey(channelId), viewerChatKey(channelId)];
}

// A line goes in by its time, not by when it arrived: an old line sent again after a restart lands in its
// own place, not under today's chat (owner 2026-10-14: "where do these come from? they are not from now").
function inOrder(feed: readonly ChatLine[], line: ChatLine): ChatLine[] {
  const at = Date.parse(line.timestamp);
  const later = feed.findIndex((existing) =>
    Date.parse(existing.timestamp) > at);
  const index = later === -1
    ? feed.length
    : later;

  return [...feed.slice(0, index), line, ...feed.slice(index)];
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

// A new line joins its channel's feed once, by its place in time; a feed never loaded has no cache to join.
function appendLine(queries: HubEventContext['queries'], line: ChatLine): void {
  for (const key of feedKeys(line.channelId)) {
    queries.setQueryData<ChatLine[]>(key, (feed) =>
      feed && !feed.some((existing) =>
        existing.id === line.id)
        ? inOrder(feed, line).slice(-chatLineLimit)
        : feed);
  }
}

// One connection watches many channels, so every handler files by the channel the payload names, not the joined one.
// A channel whose feed was never loaded has no cache; the page seeds it from the history call.
export const chatHandlers: [
  HubEventHandler<ChatMessagePayload>,
  HubEventHandler<RewardRedeemedPayload>,
  HubEventHandler<RedemptionActivityPayload>,
  HubEventHandler<ChannelEventPayload>,
  HubEventHandler<ModActionPayload>,
] = [
  {
    event: 'ChatMessage',
    handle: ({ queries }, message) => {
      appendLine(queries, message);
    },
  },
  {
    event: 'RewardRedeemed',
    handle: ({ queries }, reward) => {
      appendLine(queries, rewardLine(reward));
    },
  },
  {
    event: 'RedemptionActivity',
    handle: ({ queries }, activity) => {
      appendLine(queries, activityLine(activity));
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
