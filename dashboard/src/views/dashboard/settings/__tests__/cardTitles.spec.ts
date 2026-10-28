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

import frameEn from '@/i18n/locales/en.json';
import frameNl from '@/i18n/locales/nl.json';

import pageEn from '../i18n/en.json';
import pageNl from '../i18n/nl.json';

interface PageWords {
  frame: { frame: { nav: { settingsPages: Record<string, string> } } };
  page: { dashboard: { settings: Record<string, { title: string }> } };
}

// The page header already shows the sidebar word. The card under it adds, never repeats.
const navKeys = {
  readability: 'readability',
  basics: 'botBasics',
  personality: 'botPersonality',
  engagement: 'engagementTriggers',
} as const;

const locales: [string, PageWords][] = [
  ['en', { frame: frameEn as never, page: pageEn as never }],
  ['nl', { frame: frameNl as never, page: pageNl as never }],
];

describe('settings tab card titles', () => {
  it.each(locales)('%s: no card title restates its page title', (_locale, words) => {
    const restating = Object.entries(navKeys)
      .filter(([view, navKey]) => {
        const pageTitle = words.frame.frame.nav.settingsPages[navKey]!.toLowerCase();
        const cardTitle = words.page.dashboard.settings[view]!.title.toLowerCase();

        return pageTitle.includes(cardTitle) || cardTitle.includes(pageTitle);
      })
      .map(([view]) =>
        view);

    expect(restating).toEqual([]);
  });
});
