// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { Component } from 'vue';
import Megaphone from '~icons/nnz/megaphone';
import Bell from '~icons/nnz/bell';
import Diamond from '~icons/nnz/diamond';
import GiftCard from '~icons/nnz/gift-card';
import ShieldAlert from '~icons/nnz/shield-alert';
import Star from '~icons/nnz/star';
import Ticket from '~icons/nnz/ticket';
import UserAdd from '~icons/nnz/user-add';
import UsersSwap from '~icons/nnz/users-swap';

import type { EventResponseChange, EventResponseDetail } from '@/api/eventResponses';
import { noPipelineId, type ReplyMode } from '@/components/layout/replyMode';

// What the editor form holds. The switch for "enabled" is not here: it saves at once.
export interface EventDraft {
  mode: ReplyMode;
  message: string;
  pipelineId: string;
  speakWithTts: boolean;
}

export function draftOf(saved: EventResponseDetail): EventDraft {
  return {
    mode: saved.responseType === 'pipeline'
      ? 'full'
      : 'simple',
    message: saved.message ?? '',
    pipelineId: saved.pipelineId ?? '',
    speakWithTts: saved.speakWithTts,
  };
}

export function sameDraft(left: EventDraft, right: EventDraft): boolean {
  return (
    left.mode === right.mode &&
    left.message === right.message &&
    left.pipelineId === right.pipelineId &&
    left.speakWithTts === right.speakWithTts
  );
}

// Only the fields that differ from the saved response travel to the bot.
export function changeOf(draft: EventDraft, saved: EventResponseDetail): EventResponseChange {
  const base = draftOf(saved);
  const change: EventResponseChange = {};

  // The message stays on the response when a pipeline answers; only the response type says which one runs.
  if (draft.mode === 'simple' && draft.message !== base.message) {
    change.message = draft.message;
  }

  if (draft.mode === 'simple' && base.mode === 'full') {
    change.responseType = 'chat_message';
    change.pipelineId = noPipelineId;
  }

  const full = draft.mode === 'full' && draft.pipelineId !== '';

  if (full && (base.mode === 'simple' || draft.pipelineId !== base.pipelineId)) {
    change.responseType = 'pipeline';
    change.pipelineId = draft.pipelineId;
  }

  if (draft.speakWithTts !== base.speakWithTts) {
    change.speakWithTts = draft.speakWithTts;
  }

  return change;
}

// "channel.subscription.gift" reads "Subscription gift".
export function nameOf(eventType: string): string {
  const words = eventType
    .replace(/^channel\./, '')
    .replace(/[._]/g, ' ')
    .trim();

  return words.charAt(0).toUpperCase() + words.slice(1);
}

// The icon comes from the kind of event; the first rule that matches the event type wins.
const iconRules = [
  [/gift/, GiftCard],
  [/subscri|resub/, Star],
  [/follow/, UserAdd],
  [/cheer|bits/, Diamond],
  [/raid/, UsersSwap],
  [/redemption|reward/, Ticket],
  [/ban|timeout/, ShieldAlert],
  [/ad_break|shoutout/, Megaphone],
] as const satisfies readonly (readonly [RegExp, Component])[];

export function iconOf(eventType: string): Component {
  const rule = iconRules.find(([pattern]) => pattern.test(eventType));

  return rule?.[1] ?? Bell;
}
