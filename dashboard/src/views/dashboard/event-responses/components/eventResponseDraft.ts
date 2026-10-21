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
import { boundTarget, sameTarget, targetToWire, type PickedTarget } from '@/lib/bindingTarget';

// What the editor form holds. The switch for "enabled" is not here: it saves at once.
export interface EventDraft {
  mode: ReplyMode;
  message: string;
  target: PickedTarget | null;
  speakWithTts: boolean;
}

export function draftOf(saved: EventResponseDetail) {
  return {
    mode: saved.responseType === 'pipeline'
      ? 'full'
      : 'simple',
    message: saved.message ?? '',
    target: boundTarget(saved.target, saved.pipelineId),
    speakWithTts: saved.speakWithTts,
  } as const satisfies EventDraft;
}

export function sameDraft(left: EventDraft, right: EventDraft): boolean {
  return (
    left.mode === right.mode &&
    left.message === right.message &&
    sameTarget(left.target, right.target) &&
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

    // A command or action binding has no pipeline id to clear, so the target is cleared itself.
    if (base.target !== null && base.target.kind !== 'pipeline') {
      change.target = targetToWire(null);
    }
  }

  const bound = draft.mode === 'full'
    ? draft.target
    : null;

  if (bound !== null && (base.mode === 'simple' || !sameTarget(bound, base.target))) {
    change.responseType = 'pipeline';
    change.target = targetToWire(bound);
  }

  if (draft.speakWithTts !== base.speakWithTts) {
    change.speakWithTts = draft.speakWithTts;
  }

  return change;
}

const integrationNames = new Map([['obs', 'OBS'], ['vts', 'VTube Studio']]);

// "channel.subscription.gift" reads "Subscription gift".
// "obs.RecordStateChanged" reads "OBS record state changed".
export function nameOf(eventType: string): string {
  const [head, ...rest] = eventType.split('.');
  const product = integrationNames.get(head ?? '');

  if (product !== undefined) {
    const words = rest
      .join(' ')
      .replace(/([a-z])([A-Z])/g, '$1 $2')
      .replace(/ Event$/, '')
      .toLowerCase();

    return `${product} ${words}`;
  }

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
