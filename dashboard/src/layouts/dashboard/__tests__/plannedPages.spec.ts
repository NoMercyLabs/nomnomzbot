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

import { dashboardRoutes } from '@/router/dashboard';
import en from '@/i18n/locales/en.json';

import ledger from './fixtures/mockup-nav.json';
import { navByMode } from '../navModes';
import type { NavMode } from '../navigation';
import { useNavigation, type Navigation } from '../useNavigation';

// The ledger is the mockup navigation (base/shell.js NAV and 4-settings.html), extracted by script.
type Ledger = Record<NavMode, { group: string; entries: { label: string; icon: string }[] }[]>;

const planned = ledger as Ledger;
const MODES = ['dashboard', 'admin', 'viewer', 'settings'] as const satisfies readonly NavMode[];

// Dashboard groups the owner asked for that the mockup ledger does not list (Discord, 2026-10-15).
const beyondMockup: ReadonlySet<string> = new Set(['connect']);

// Single pages the owner asked for that the mockup ledger does not list (all polls and predictions, 2026-10-15;
// the viewer chat, 2026-10-16).
const pagesBeyondMockup: ReadonlySet<string> = new Set([
  'dashboard-polls',
  'dashboard-viewer-chat',
  'dashboard-voice-triggers',
]);

const stub = defineComponent({ render: () => h('div') });

// The real route table, without the admin guard and the lazy page loaders, so the sidebar reads real names and meta.
// The navigation asks the bot what the person may read; these tests cover the frame, so nothing is known yet and nothing is hidden.
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
  const aliases: Record<string, string> = { tts: 'text to speech' };
  const norm = (text: string): string => text.toLowerCase().replace(/&/g, 'and');

  return norm(shown) === (aliases[norm(mockup)] ?? norm(mockup));
}

beforeEach(() => {
  localStorage.clear();
  window.dispatchEvent(new StorageEvent('storage', { key: 'nnz.nav.folds', storageArea: localStorage }));
});

afterEach(() => localStorage.clear());

describe('every planned page is reachable from its sidebar', () => {
  it('plans 72 pages in the ledger', () => {
    const total = MODES.reduce(
      (sum, mode) => sum + planned[mode].reduce((n, group) => n + group.entries.length, 0),
      0,
    );

    expect(total).toBe(72);
  });

  for (const mode of MODES) {
    describe(`the ${mode} sidebar`, () => {
      it('has a route in the right mode for every planned entry, in the mockup order', async () => {
        const router = realRouter();
        const navigation = await openMode(router, mode);
        const shown = navigation.groups.value
          .filter((group) => group.key !== 'back' && !beyondMockup.has(group.key))
          .map((group) => ({
            ...group,
            entries: group.entries.filter((entry) => !pagesBeyondMockup.has(entry.route)),
          }));

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
