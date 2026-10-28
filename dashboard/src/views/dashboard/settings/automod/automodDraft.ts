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

import type { AutomodConfig } from '@/api/automod';

// What the page edits: the four filters. A list is one entry per line while it is typed.
export type AutomodDraft = {
  linksOn: boolean;
  allowedDomains: string;
  capsOn: boolean;
  capsThreshold: number;
  phrasesOn: boolean;
  phrases: string;
  emotesOn: boolean;
  maxEmotes: number;
};

export const CAPS_RANGE = { min: 1, max: 100 } as const;
export const EMOTES_RANGE = { min: 1, max: 50 } as const;

const lines = (list: readonly string[]): string => list.join('\n');

// One entry per line; blank lines and doubled entries drop out.
export function toList(text: string): string[] {
  const entries = text.split('\n').map((line) => line.trim()).filter((line) => line !== '');

  return [...new Set(entries)];
}

export function toDraft(config: AutomodConfig) {
  return {
    linksOn: config.linkFilter.enabled,
    allowedDomains: lines(config.linkFilter.whitelist),
    capsOn: config.capsFilter.enabled,
    capsThreshold: Number(config.capsFilter.threshold),
    phrasesOn: config.bannedPhrases.enabled,
    phrases: lines(config.bannedPhrases.phrases),
    emotesOn: config.emoteSpam.enabled,
    maxEmotes: Number(config.emoteSpam.maxEmotes),
  } as const satisfies AutomodDraft;
}

// The whole record goes back, so what the page does not edit (the heat settings) stays as the bot sent it.
export function toConfig(draft: AutomodDraft, loaded: AutomodConfig) {
  return {
    ...loaded,
    linkFilter: { enabled: draft.linksOn, whitelist: toList(draft.allowedDomains) },
    capsFilter: { enabled: draft.capsOn, threshold: draft.capsThreshold },
    bannedPhrases: { enabled: draft.phrasesOn, phrases: toList(draft.phrases) },
    emoteSpam: { enabled: draft.emotesOn, maxEmotes: draft.maxEmotes },
  } as const satisfies AutomodConfig;
}

export function sameDraft(a: AutomodDraft, b: AutomodDraft): boolean {
  return JSON.stringify(a) === JSON.stringify(b);
}

// A number outside its range is refused before the bot sees it.
export function problems(draft: AutomodDraft) {
  return {
    ...(draft.capsThreshold < CAPS_RANGE.min || draft.capsThreshold > CAPS_RANGE.max
      ? { capsThreshold: true as const }
      : {}),
    ...(draft.maxEmotes < EMOTES_RANGE.min || draft.maxEmotes > EMOTES_RANGE.max
      ? { maxEmotes: true as const }
      : {}),
  } as const satisfies Partial<Record<'capsThreshold' | 'maxEmotes', true>>;
}
