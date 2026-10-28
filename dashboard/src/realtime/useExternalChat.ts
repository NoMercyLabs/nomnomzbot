// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

import { onScopeDispose, ref, type Ref } from 'vue';

import type { ChatLine } from '@/api/chat';
import { chatLineLimit } from '@/api/chat';

import { joinTwitchChat, leaveTwitchChat, sendTwitchChat, startDashboardHub } from './dashboardHub';
import { onExternalChat, toChatLine, type ExternalChatPayload } from './externalChat';
import { queryClient } from '@/api/queryClient';

export const writeScope = 'user:write:chat';
export const readScope = 'user:read:chat';

export type ExternalChat = {
  lines: Ref<ChatLine[]>;
  // The Twitch permission the viewer's token lacks, or null.
  missingScope: Ref<string | null>;
  failure: Ref<string | null>;
  send: (message: string, replyToMessageId?: string | null) => Promise<boolean>;
};

// One Twitch channel the bot does not serve, read and written on the viewer's own token. It joins when the
// page opens and leaves when the page closes (chat-client.md 9a "Any Twitch channel"). No history exists.
export function useExternalChat(twitchBroadcasterId: string) {
  const lines = ref<ChatLine[]>([]);
  const missingScope = ref<string | null>(null);
  const failure = ref<string | null>(null);

  function apply(payload: ExternalChatPayload): void {
    if (payload.broadcasterTwitchId !== twitchBroadcasterId) return;

    if (payload.kind === 'ChatCleared') {
      lines.value = [];

      return;
    }

    if (payload.kind === 'MessageDeleted') {
      lines.value = lines.value.filter((line) =>
        line.id !== payload.messageId);

      return;
    }

    lines.value = [...lines.value, toChatLine(payload)].slice(-chatLineLimit);
  }

  const stopListening = onExternalChat(apply);

  void open();

  async function open(): Promise<void> {
    await startDashboardHub(queryClient);

    const answer = await joinTwitchChat(twitchBroadcasterId);

    if (!answer.success) {
      failure.value = answer.error;

      missingScope.value = answer.errorCode === 'missing_scope'
        ? answer.missingScope ?? readScope
        : null;
    }
  }

  async function send(message: string, replyToMessageId: string | null = null): Promise<boolean> {
    const answer = await sendTwitchChat(twitchBroadcasterId, message, replyToMessageId);

    if (answer.success) {
      missingScope.value = null;

      return true;
    }

    // The error code is the sign of a missing permission, never the words.
    missingScope.value = answer.errorCode === 'missing_scope'
      ? answer.missingScope ?? writeScope
      : null;

    failure.value = answer.error;

    return false;
  }

  onScopeDispose(() => {
    stopListening();
    void leaveTwitchChat(twitchBroadcasterId);
  });

  return {
    lines,
    missingScope,
    failure,
    send,
  } as const satisfies ExternalChat;
}
