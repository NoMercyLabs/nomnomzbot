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

import type { components } from '@/api/schema';

// How an editor answers: with its own simple reply text, or by running a full pipeline that has its own reply step.
export type ReplyMode = 'simple' | 'full';

// Where a simple reply goes: Twitch chat, the chat overlay, both, or nowhere. The bot reads no setting (null) as chat.
export type ReplyDelivery = components['schemas']['ReplyDelivery'];
export type ReplyTarget = components['schemas']['ReplyTarget'];

export const replyTargets = ['chat', 'overlay', 'both', 'none'] as const satisfies readonly ReplyTarget[];

// How the overlay line looks: a design token for the highlight, a size, and how long the line stays.
export type ReplyLook = components['schemas']['ReplyLook'];
export type ReplyHighlight = NonNullable<components['schemas']['ReplyHighlight']>;
export type ReplySize = components['schemas']['ReplySize'];

export const replyHighlights = ['primary', 'success', 'warning', 'destructive'] as const satisfies readonly ReplyHighlight[];
export const replySizes = ['normal', 'large'] as const satisfies readonly ReplySize[];

// The longest time a line may stay, in seconds (the bot refuses more, and anything under 1).
export const maxLookSeconds = 120;

// The look with every part spelled out: no highlight (null), normal size and no own duration (null) are the defaults.
export interface ReplyLookView {
  highlight: ReplyHighlight | null;
  size: ReplySize;
  durationSeconds: number | null;
}

export const defaultLook = {
  highlight: null,
  size: 'normal',
  durationSeconds: null,
} as const satisfies ReplyLookView;

export const plainDelivery = {
  target: 'chat',
  html: false,
} as const satisfies ReplyDelivery;

// A reply shows on the overlay for these targets only, so HTML and the look mean something for these only.
export function reachesOverlay(target: ReplyTarget): boolean {
  return target === 'overlay' || target === 'both';
}

export function targetOf(delivery: ReplyDelivery): ReplyTarget {
  return delivery.target ?? 'chat';
}

export function lookOf(delivery: ReplyDelivery) {
  const seconds = delivery.look?.durationSeconds;

  return {
    highlight: delivery.look?.highlight ?? null,
    size: delivery.look?.size ?? 'normal',
    durationSeconds: seconds === null || seconds === undefined
      ? null
      : Number(seconds),
  } as const satisfies ReplyLookView;
}

function isDefaultLook(look: ReplyLookView): boolean {
  return look.highlight === null && look.size === 'normal' && look.durationSeconds === null;
}

// Only the parts that differ from the default are sent, so a row that never chose a look stays without one.
function sparseLook(look: ReplyLookView) {
  return {
    ...(look.highlight === null
      ? {}
      : { highlight: look.highlight }),
    ...(look.size === 'normal'
      ? {}
      : { size: look.size }),
    ...(look.durationSeconds === null
      ? {}
      : { durationSeconds: look.durationSeconds }),
  } as const satisfies ReplyLook;
}

// The delivery carrying this look. A default look, or a target that never reaches the overlay, leaves no look key.
export function withLook(delivery: ReplyDelivery, look: ReplyLookView) {
  const base = {
    target: targetOf(delivery),
    html: delivery.html,
  } as const satisfies ReplyDelivery;

  return isDefaultLook(look) || !reachesOverlay(base.target)
    ? base
    : {
      ...base,
      look: sparseLook(look),
    } as const satisfies ReplyDelivery;
}

// What an editor form holds: a concrete delivery, plain Twitch chat when the row has none.
export function deliveryOf(saved: ReplyDelivery | null | undefined) {
  return saved === null || saved === undefined
    ? { ...plainDelivery } as const satisfies ReplyDelivery
    : withLook(saved, lookOf(saved));
}

export function sameDelivery(left: ReplyDelivery, right: ReplyDelivery): boolean {
  const leftLook = lookOf(left);
  const rightLook = lookOf(right);

  return targetOf(left) === targetOf(right) &&
    left.html === right.html &&
    leftLook.highlight === rightLook.highlight &&
    leftLook.size === rightLook.size &&
    leftLook.durationSeconds === rightLook.durationSeconds;
}

// The bot reads a null delivery on an update as "unchanged", so going back to Twitch chat must send chat itself.
export function deliveryToSave(draft: ReplyDelivery): ReplyDelivery {
  return withLook(draft, lookOf(draft));
}

// A pipeline that is picked sends this id. The bot reads the empty id as "no pipeline" (the sentinel of its PUT calls).
export const noPipelineId = '00000000-0000-0000-0000-000000000000';
