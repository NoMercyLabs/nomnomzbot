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

// What a live run would really do, for the confirm: the action steps of the draft grouped by the category the
// bot files each action under. Moderation and economy touch real viewers or real currency.
import type { PipelineActionDescriptor, PipelineStep } from '@/api/pipelines';

import { actionTitle, type Words } from './stepWords';

export type LiveGroupKey = 'chat' | 'overlay' | 'moderation' | 'economy' | 'other';

export type LiveStep = {
  id: string | null;
  title: string;
  // The name inside the template when a user field holds one, such as {args.1}; null when no user is a template.
  hitsWhoever: string | null;
};

export type LiveGroup = {
  key: LiveGroupKey;
  steps: LiveStep[];
  // True when the steps act on real viewers or real currency.
  real: boolean;
};

export const LIVE_GROUPS = [
  'chat',
  'overlay',
  'moderation',
  'economy',
  'other',
] as const satisfies readonly LiveGroupKey[];

const GROUP_OF = {
  chat: 'chat',
  sound: 'overlay',
  tts: 'overlay',
  widgets: 'overlay',
  media: 'overlay',
  obs: 'overlay',
  vts: 'overlay',
  moderation: 'moderation',
  economy: 'economy',
  billing: 'economy',
} as const satisfies Record<string, LiveGroupKey>;

const REAL = new Set<LiveGroupKey>(['moderation', 'economy']);

const TEMPLATE = /\{[^{}]+\}/;

function groupOf(descriptor: PipelineActionDescriptor | undefined): LiveGroupKey {
  const name = descriptor?.category.key?.split('.').at(-1) ?? '';

  return (GROUP_OF as Record<string, LiveGroupKey | undefined>)[name] ?? 'other';
}

// A user field (a viewer the action hits) whose saved value is a template names its target at run time.
function templatedTarget(
  step: PipelineStep,
  descriptor: PipelineActionDescriptor | undefined,
): string | null {
  for (const field of descriptor?.fields ?? []) {
    const saved = step.action[field.name];
    const found = field.kind === 'twitch_user' && typeof saved === 'string'
      ? TEMPLATE.exec(saved)
      : null;

    if (found)
      return found[0];
  }

  return null;
}

export function livePlan(
  steps: readonly PipelineStep[],
  descriptors: ReadonlyMap<string, PipelineActionDescriptor>,
  words: Words,
): LiveGroup[] {
  const found = new Map<LiveGroupKey, LiveStep[]>();

  for (const step of steps) {
    if (step.block_kind !== null) continue;

    const descriptor = descriptors.get(step.action.type);
    const key = groupOf(descriptor);
    const list = found.get(key) ?? [];

    list.push({
      id: step.id,
      title: actionTitle(step.action.type, words),
      hitsWhoever: templatedTarget(step, descriptor),
    });

    found.set(key, list);
  }

  return LIVE_GROUPS
    .filter((key) => found.has(key))
    .map((key) => ({
      key,
      steps: found.get(key) ?? [],
      real: REAL.has(key),
    }));
}
