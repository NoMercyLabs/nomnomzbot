// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { components } from '@/api/schema';

// How an editor answers: with its own simple reply text, or by running a full pipeline that has its own reply step.
export type ReplyMode = 'simple' | 'full';

// Where a simple reply goes: Twitch chat, the chat overlay, both, or nowhere. The bot reads no setting (null) as chat.
export type ReplyDelivery = components['schemas']['ReplyDelivery'];
export type ReplyTarget = components['schemas']['ReplyTarget'];

export const replyTargets = ['chat', 'overlay', 'both', 'none'] as const satisfies readonly ReplyTarget[];

export const plainDelivery = {
  target: 'chat',
  html: false,
} as const satisfies ReplyDelivery;

// A reply shows on the overlay for these targets only, so HTML means something for these only.
export function reachesOverlay(target: ReplyTarget): boolean {
  return target === 'overlay' || target === 'both';
}

export function targetOf(delivery: ReplyDelivery): ReplyTarget {
  return delivery.target ?? 'chat';
}

// What an editor form holds: a concrete delivery, plain Twitch chat when the row has none.
export function deliveryOf(saved: ReplyDelivery | null | undefined) {
  return saved === null || saved === undefined
    ? { ...plainDelivery } as const satisfies ReplyDelivery
    : {
      target: targetOf(saved),
      html: saved.html,
    } as const satisfies ReplyDelivery;
}

export function sameDelivery(left: ReplyDelivery, right: ReplyDelivery): boolean {
  return targetOf(left) === targetOf(right) && left.html === right.html;
}

// Plain Twitch chat travels as null: the command keeps the behavior it had before the setting existed.
export function deliveryToSave(draft: ReplyDelivery): ReplyDelivery | null {
  return sameDelivery(draft, plainDelivery)
    ? null
    : draft;
}

// A pipeline that is picked sends this id. The bot reads the empty id as "no pipeline" (the sentinel of its PUT calls).
export const noPipelineId = '00000000-0000-0000-0000-000000000000';
