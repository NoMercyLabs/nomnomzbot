// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { flushPromises, mount } from '@vue/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { computed, defineComponent, h, ref } from 'vue';
import { createMemoryHistory, createRouter, type RouteRecordRaw, type Router } from 'vue-router';

import { lookup } from '@/lib/array';
import { dashboardRoutes } from '@/router/dashboard';
import en from '@/i18n/locales/en.json';

import ledger from './fixtures/mockup-nav.json';
import { navByMode } from '../navModes';
import type { NavMode } from '../navigation';
import { useNavigation, type Navigation } from '../useNavigation';

// The ledger is the mockup navigation (base/shell.js NAV and 4-settings.html), extracted by script.
type Ledger = Record<NavMode, { group: string; entries: { label: string; icon: string }[] }[]>;

const planned = ledger as Ledger;
// The viewer pages sit under /me/:channel with their own navigation (views/viewer/nav), not in a sidebar here.
const MODES = ['dashboard', 'admin', 'settings'] as const satisfies readonly NavMode[];

// Dashboard groups the owner asked for that the mockup ledger does not list.
const beyondMockup: ReadonlySet<string> = new Set([]);

// Single pages the owner asked for that the mockup ledger does not list (all polls and predictions, 2026-10-15;
// the viewer chat, 2026-10-16; the quotes, 2026-10-18).
const pagesBeyondMockup: ReadonlySet<string> = new Set([
  'dashboard-pick-lists',
  'dashboard-polls',
  'dashboard-quotes',
  'dashboard-sounds',
  // Discord, 2026-10-15; moved into the settings 2026-10-20.
  'dashboard-settings-discord',
  'dashboard-settings-automod',
  'dashboard-settings-chat-filters',
  'dashboard-settings-shoutouts',
  'dashboard-settings-blocked-terms',
  'dashboard-settings-escalation',
  'dashboard-settings-spam-defense',
  'dashboard-settings-trust-policy',
  'dashboard-viewer-chat',
  'dashboard-voice-triggers',
]);

const stub = defineComponent({ render: () => h('div') });

// The real route table, without the admin guard and the lazy page loaders, so the sidebar reads real names and meta.
// The navigation asks the bot what the person may read; these tests cover the frame, so nothing is known yet and nothing is hidden.
// The bot has not told the platform keys yet, so no admin entry is hidden.
vi.mock('@/composables/usePlatformCan', () => ({
  usePlatformCan: () => ({ known: { value: false }, can: () => false }),
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref(null) }),
}));

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({ known: computed(() => false), can: () => false }),
}));

function realRouter(): Router {
  const children: RouteRecordRaw[] = (dashboardRoutes.children ?? []).map(
    (child) => ({
      ...child,
      beforeEnter: undefined,
      component: stub,
    }) as RouteRecordRaw,
  );

  return createRouter({
    history: createMemoryHistory(),
    routes: children.map((child) => ({ ...child, path: `/app/${child.path}` })),
  });
}

async function openMode(router: Router, mode: NavMode): Promise<Navigation> {
  const pages = navByMode[mode].filter((group) => group.key !== 'back');
  const first = pages.flatMap((group) => group.entries).find((e) => router.hasRoute(e.route));

  await router.push({ name: first?.route ?? 'missing' });
  let navigation: Navigation | undefined;
  const host = defineComponent({
    setup() {
      navigation = useNavigation();

      return () => h('div');
    },
  });

  mount(host, { global: { plugins: [router] } });
  await flushPromises();

  return navigation as Navigation;
}

function words(path: string): string {
  return path.split('.').reduce<unknown>((node, key) => (node as Record<string, unknown>)[key], en) as string;
}

// The mockup says "TTS" and "&"; the shipped words spell them out.
function same(shown: string, mockup: string): boolean {
  const aliases = { tts: 'text to speech' } as const satisfies Record<string, string>;
  const norm = (text: string): string => text.toLowerCase().replace(/&/g, 'and');

  return norm(shown) === (lookup(aliases, norm(mockup)) ?? norm(mockup));
}

beforeEach(() => {
  localStorage.clear();
  window.dispatchEvent(new StorageEvent('storage', { key: 'nnz.nav.folds', storageArea: localStorage }));
});

afterEach(() => localStorage.clear());

describe('every planned page is reachable from its sidebar', () => {
  it('plans 65 sidebar pages in the ledger', () => {
    const total = MODES.reduce(
      (sum, mode) => sum + planned[mode].reduce((n, group) => n + group.entries.length, 0),
      0,
    );

    expect(total).toBe(65);
  });

  for (const mode of MODES) {
    describe(`the ${mode} sidebar`, () => {
      it('has a route in the right mode for every planned entry, in the mockup order', async () => {
        const router = realRouter();
        const navigation = await openMode(router, mode);
        // The owner moved People and Music and voice up the sidebar (2026-10-22), so each group is matched to
        // its mockup group by title and the entries inside a group keep the mockup order.
        const ledgerPlace = (key: string): number =>
          planned[mode].findIndex((group) => same(words(`frame.group.${key}`), group.group));
        const shown = navigation.groups.value
          .filter((group) => group.key !== 'back' && !beyondMockup.has(group.key))
          .map((group) => ({
            ...group,
            entries: group.entries.filter((entry) => !pagesBeyondMockup.has(entry.route)),
          }))
          .sort((a, b) => ledgerPlace(a.key) - ledgerPlace(b.key));

        // Group by group: the same number of groups, the same titles, the same entry count.
        expect(shown.map((group) => group.entries.length)).toEqual(
          planned[mode].map((group) => group.entries.length),
        );

        const titles = shown.map((group) => words(`frame.group.${group.key}`));
        const wanted = planned[mode].filter((group) => group.group !== '');

        expect(wanted.map((group) => same(titles[planned[mode].indexOf(group)] ?? '', group.group))).toEqual(
          wanted.map(() => true),
        );

        // Entry by entry, in order: the label, an icon, and the sidebar the page opens in.
        const entries = shown.flatMap((group) => group.entries);
        const expectedEntries = planned[mode].flatMap((group) => group.entries);

        expect(entries.map((entry) => words(`frame.nav.${entry.label}`)).map((text, at) =>
          same(text, expectedEntries[at]?.label ?? ''))).toEqual(entries.map(() => true));

        expect(entries.map((entry) => entry.icon !== undefined)).toEqual(entries.map(() => true));

        // The dashboard's Settings entry is the door to the settings sidebar.
        const landsIn = entries.map((entry) => router.resolve({ name: entry.route }).meta.navMode ?? 'dashboard');
        const door = (entry: { route: string }): NavMode =>
          entry.route === 'dashboard-settings' && mode === 'dashboard'
            ? 'settings'
            : mode;

        expect(landsIn).toEqual(entries.map(door));
      });
    });
  }
});
