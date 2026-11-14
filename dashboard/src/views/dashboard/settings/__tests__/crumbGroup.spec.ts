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

import { settingsNav } from '@/layouts/dashboard/settingsNavigation';

// Page sources, as text, for the settings pages that sit in a named sidebar group.
const pages: Record<string, string> = import.meta.glob(
  [
    '../api-devices/ApiDevicesView.vue',
    '../webhooks/WebhooksView.vue',
    '../federation/FederationView.vue',
    '../../discord/DiscordView.vue',
    '../../supporters/SupporterSourcesView.vue',
  ],
  {
    eager: true,
    query: '?raw',
    import: 'default',
  },
);

const sourceOf = (file: string): string =>
  Object.entries(pages).find(([path]) => path.endsWith(file))?.[1] ?? '';

const PAGES = {
  'dashboard-settings-api-devices': 'ApiDevicesView.vue',
  'dashboard-settings-webhooks': 'WebhooksView.vue',
  'dashboard-settings-federation': 'FederationView.vue',
  'dashboard-settings-discord': 'DiscordView.vue',
  'dashboard-settings-supporter-sources': 'SupporterSourcesView.vue',
} as const satisfies Record<string, string>;

// The crumb of a settings page names the sidebar group that holds the page, so the sidebar and the header agree.
describe('the crumb of a settings page in a named sidebar group', () => {
  for (const [routeName, file] of Object.entries(PAGES)) {
    it(`${file} shows the group word of its sidebar group as the crumb`, () => {
      const group = settingsNav.find((candidate) =>
        candidate.entries.some((entry) => entry.route === routeName));

      expect(group, `${routeName} is in a sidebar group`).toBeDefined();
      expect(sourceOf(file)).toContain(`'frame.group.${group?.key}'`);
    });
  }
});
