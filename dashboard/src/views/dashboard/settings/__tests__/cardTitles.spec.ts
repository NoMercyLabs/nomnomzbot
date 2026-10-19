// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
