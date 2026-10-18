// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, h } from 'vue';
import { createMemoryHistory, createRouter, type Router } from 'vue-router';

import { i18n } from '@/i18n';
import { settingsNav } from '@/layouts/dashboard/settingsNavigation';

import en from '../i18n/en.json';
import SettingsView from '../SettingsView.vue';
import { dashboardSettingsRoutes } from '../route';

vi.mock('../BasicsView.vue', () => ({
  __esModule: true,
  default: defineComponent({ render: () => h('div', { 'data-view': 'basics' }) }),
}));

vi.mock('../PersonalityView.vue', () => ({
  __esModule: true,
  default: defineComponent({ render: () => h('div', { 'data-view': 'personality' }) }),
}));

vi.mock('../EngagementView.vue', () => ({
  __esModule: true,
  default: defineComponent({ render: () => h('div', { 'data-view': 'engagement' }) }),
}));

vi.mock('../ReadabilityView.vue', () => ({
  __esModule: true,
  default: defineComponent({ render: () => h('div', { 'data-view': 'readability' }) }),
}));

let mounted: VueWrapper | null = null;

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

const routeOf = (name: string) => dashboardSettingsRoutes.find((route) => route.name === name);

async function open(name: string): Promise<Router> {
  const record = routeOf(name)!;
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{
      path: `/app/${record.path}`,
      name,
      component: SettingsView,
      props: record.props,
    }],
  });

  i18n.global.mergeLocaleMessage('en', en);
  await router.push({ name });

  mounted = mount(SettingsView, {
    attachTo: document.body,
    props: record.props as { view: 'readability' },
    global: { plugins: [i18n, router] },
  });

  await flushPromises();

  return router;
}

describe('Settings sidebar routes', () => {
  const pages = [
    ['dashboard-settings-bot-basics', 'basics'],
    ['dashboard-settings-bot-personality', 'personality'],
    ['dashboard-settings-engagement-triggers', 'engagement'],
  ] as const;

  it.each(pages)('the sidebar entry %s has its own route that is not a coming-soon page', (name) => {
    const entry = settingsNav.flatMap((group) => group.entries).find((item) => item.route === name);
    const record = routeOf(name);

    expect(entry).toBeDefined();
    expect(record).toBeDefined();
    expect(record!.meta).toMatchObject({ navMode: 'settings' });
    expect(record!.meta?.comingSoon).toBeUndefined();
  });

  it.each(pages)('%s opens only its own page', async (name, view) => {
    await open(name);

    expect(document.body.querySelector(`[data-view="${view}"]`)).not.toBeNull();
    expect(document.body.querySelectorAll('[data-view]')).toHaveLength(1);
  });

  it('the Readability page is only Readability: no inner menu, no Channel or This device tab', async () => {
    await open('dashboard-settings');

    expect(document.body.querySelector('[data-view="readability"]')).not.toBeNull();
    expect(document.body.querySelector('[data-slot="list-detail-list"]')).toBeNull();
    expect(document.body.textContent).not.toContain('This device');
    expect(document.body.textContent).not.toContain('Greetings and streaks');
    expect(document.body.querySelector('[role="tablist"]')).toBeNull();
  });
});
