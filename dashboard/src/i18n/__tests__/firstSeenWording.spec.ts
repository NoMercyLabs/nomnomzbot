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

import { describe, expect, it } from 'vitest';

import chatEn from '@/views/dashboard/chat/i18n/en.json?raw';
import chatNl from '@/views/dashboard/chat/i18n/nl.json?raw';
import homeEn from '@/views/dashboard/home/i18n/en.json?raw';
import homeNl from '@/views/dashboard/home/i18n/nl.json?raw';
import journalEn from '@/views/dashboard/settings/event-journal/i18n/en.json?raw';
import journalNl from '@/views/dashboard/settings/event-journal/i18n/nl.json?raw';
import settingsEn from '@/views/dashboard/settings/i18n/en.json?raw';
import settingsNl from '@/views/dashboard/settings/i18n/nl.json?raw';
import webhooksEn from '@/views/dashboard/settings/webhooks/i18n/en.json?raw';
import webhooksNl from '@/views/dashboard/settings/webhooks/i18n/nl.json?raw';

// Twitch sends no first-message flag: the bot only knows when it first sees a viewer.
const labelKeys = [
  'hintFirstEver',
  'firstEver',
  'engagement_first_time_chatter',
  'FirstTimeChatterDetectedEvent',
  'UserFirstChatEvent',
  'webhook_event_first_time_chatter_detected_event_label',
] as const;

const sources = [
  {
    name: 'chat',
    en: chatEn,
    nl: chatNl,
    keys: ['hintFirstEver', 'firstEver'],
  },
  {
    name: 'home',
    en: homeEn,
    nl: homeNl,
    keys: ['engagement_first_time_chatter'],
  },
  {
    name: 'event-journal',
    en: journalEn,
    nl: journalNl,
    keys: ['FirstTimeChatterDetectedEvent', 'UserFirstChatEvent'],
  },
  {
    name: 'webhooks',
    en: webhooksEn,
    nl: webhooksNl,
    keys: ['webhook_event_first_time_chatter_detected_event_label'],
  },
] as const;

function collectValues(raw: string, wanted: readonly string[]): string[] {
  const found: string[] = [];

  JSON.parse(raw, (key, value) => {
    if (wanted.includes(key) && typeof value === 'string') {
      found.push(value);
    }

    return value;
  });

  return found;
}

function firstTimeEntry(raw: string): { title: string; description: string } | undefined {
  let entry: { title: string; description: string } | undefined;

  JSON.parse(raw, (key, value) => {
    if (key === 'firstTime' && typeof value === 'object' && 'description' in value) {
      entry = value;
    }

    return value;
  });

  return entry;
}

const claimsEver = /\bever\b|\booit\b|allereerste/i;
const namesBot = /\bbot\b/i;

describe('first-seen wording', () => {
  for (const source of sources) {
    for (const [locale, raw] of [
      ['en', source.en],
      ['nl', source.nl],
    ] as const) {
      it(`${source.name} ${locale}: labels say the bot first sees the viewer`, () => {
        const values = collectValues(raw, source.keys);

        expect(values).toHaveLength(source.keys.length);

        for (const value of values) {
          expect(value).not.toMatch(claimsEver);
          expect(value).toMatch(namesBot);
        }
      });
    }
  }

  for (const [locale, raw] of [
    ['en', settingsEn],
    ['nl', settingsNl],
  ] as const) {
    it(`settings ${locale}: first-time greeting names the bot and claims no "ever"`, () => {
      const entry = firstTimeEntry(raw);

      expect(entry).toBeDefined();
      expect(entry?.title).not.toMatch(claimsEver);
      expect(entry?.title).toMatch(namesBot);
      expect(entry?.description).not.toMatch(claimsEver);
      expect(entry?.description).toMatch(namesBot);
    });
  }

  it('covers every label key', () => {
    const covered = sources.flatMap((source) => [...source.keys]).sort();

    expect(covered).toEqual([...labelKeys].sort());
  });
});
