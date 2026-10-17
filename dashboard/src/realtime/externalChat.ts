// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { ChatLine } from '@/api/chat';

import type { HubEventHandler } from './hubEvent';

// Application/Chat/Dtos/ExternalChatEvent.cs: one live line from a Twitch channel this deployment does not serve.
// It reaches this connection only and is not part of the OpenAPI contract, so the shape is written here.
export type ExternalChatPayload = {
  kind: 'Message' | 'MessageDeleted' | 'ChatCleared';
  broadcasterTwitchId: string;
  messageId: string | null;
  chatterTwitchId: string | null;
  chatterLogin: string | null;
  chatterDisplayName: string | null;
  color: string | null;
  text: string;
  fragments: ChatLine['fragments'];
  badges: Array<{
    setId: string;
    id: string;
    info?: string | null;
  }>;
  replyParentMessageId: string | null;
  timestamp: string;
};

type Listener = (payload: ExternalChatPayload) => void;

const listeners = new Set<Listener>();

// The page that reads one external channel listens here for as long as it is open.
export function onExternalChat(listener: Listener): () => void {
  listeners.add(listener);

  return () => {
    listeners.delete(listener);
  };
}

export const externalChatHandlers = [
  {
    event: 'ExternalChatEvent',
    handle: (_context, payload) => {
      for (const listener of listeners) {
        listener(payload);
      }
    },
  },
] as const satisfies readonly HubEventHandler<ExternalChatPayload>[];

// A pushed line as the chat line the existing row renders. The viewer holds no role in a channel the bot
// does not serve, so no role flag is set; the server sends no badge images, so a badge has no URLs.
export function toChatLine(payload: ExternalChatPayload): ChatLine {
  const fragments: ChatLine['fragments'] = payload.fragments.length > 0
    ? payload.fragments
    : [
        {
          type: 'text',
          text: payload.text,
          emote: null,
          cheermote: null,
          mention: null,
          linkPreview: null,
        },
      ];

  return {
    id: payload.messageId ?? '',
    channelId: payload.broadcasterTwitchId,
    userId: payload.chatterTwitchId ?? '',
    displayName: payload.chatterDisplayName ?? payload.chatterLogin ?? '',
    username: payload.chatterLogin ?? '',
    message: payload.text,
    fragments,
    userType: '',
    isSubscriber: false,
    isVip: false,
    isModerator: false,
    isBroadcaster: false,
    isCheer: false,
    isCommand: false,
    badges: payload.badges.map((badge) => ({ ...badge, urls: {} })),
    bitsAmount: 0,
    color: payload.color,
    messageType: 'text',
    replyToMessageId: payload.replyParentMessageId,
    timestamp: payload.timestamp,
    provider: 'twitch',
  };
}
