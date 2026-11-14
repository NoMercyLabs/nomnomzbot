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

import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia, type Pinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ref } from 'vue';
import { createMemoryHistory, createRouter, type Router } from 'vue-router';

import { i18n } from '@/i18n';

import FilterBar from '@/components/layout/FilterBar.vue';
import PageTabs from '@/components/layout/PageTabs.vue';

import CatalogView from '../CatalogView.vue';
import en from '../i18n/en.json';

vi.mock('@/api/community', () => ({
  useCommunityFirstPage: () => ({
    data: ref([]),
    isError: ref(false),
    isPending: ref(false),
    refetch: async (): Promise<void> => undefined,
  }),
}));

vi.mock('@/api/codeScripts', () => ({
  useCodeScripts: () => ({ data: ref([]), error: ref(null) }),
  fetchScriptProject: async () => null,
  fetchScriptSdkTypes: async () => null,
  saveScriptProject: async () => ({ ok: true }),
}));

// The catalog holds every key, so a tab with a lock and reason can still be shown by a page that needs one.
vi.mock('@/composables/useCan', () => ({
  useCan: () => ({ can: (): boolean => true }),
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;
let pinia: Pinia;
let router: Router;

beforeEach(() => {
  localStorage.clear();
  pinia = createPinia();
  setActivePinia(pinia);
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

async function open(): Promise<void> {
  const blank = { template: '<div />' };

  router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/app/catalog',
        name: 'dashboard-catalog',
        component: blank,
      },
    ],
  });

  await router.push('/app/catalog?view=blocks');

  mounted = mount(CatalogView, {
    attachTo: document.body,
    global: {
      plugins: [i18n, pinia, router],
      // HelperPanel declares its target prop: an undeclared one falls through as an attribute, and a
      // component instance cannot be written as one.
      stubs: {
        CodeEditor: true,
        ChartPanel: true,
        TemplateField: true,
        HelperPanel: { props: ['target', 'context'], template: '<div />' },
      },
    },
  });

  await flushPromises();
}

// The page has two FilterBars; each one's value is the v-model the page remembers.
const bar = (label: string): VueWrapper =>
  mounted!
    .findAllComponents(FilterBar)
    .find((found) => found.props('label') === label) as VueWrapper;

describe('CatalogView on a phone', () => {
  it('shows no developer text about the open tab', async () => {
    await open();

    expect(mounted!.text()).not.toContain('Open tab');
  });

  it('keeps the page in one scroll below md: the body box scrolls only from md up', async () => {
    await open();

    const body = mounted!.element.lastElementChild as HTMLElement;

    expect(body.classList.contains('overflow-y-auto')).toBe(false);
    expect(body.classList.contains('md:overflow-y-auto')).toBe(true);
  });

  it('gives the states and layouts views a natural height below md', async () => {
    await open();
    await router.push('/app/catalog?view=states');
    await flushPromises();

    const fixed = mounted!.findAll('div').filter((box) => box.classes().includes('h-72'));

    expect(fixed).toHaveLength(0);
  });
});

const SECTIONS_BY_VIEW = {
  blocks: ['stats', 'filters', 'dialogs', 'code-editor', 'flows', 'charts', 'template', 'lists'],
  states: ['panel-states'],
  layouts: ['layouts'],
  feedback: ['empty-states', 'notices', 'connections'],
  rows: ['rows', 'editors'],
  pages: [
    'page-body',
    'page-body-set',
    'settings-frame',
    'admin-collection',
    'task-shell',
    'setup-shell',
    'viewer-page',
  ],
  guide: ['guide-tiers', 'guide-actions', 'guide-filters', 'guide-settings'],
} as const satisfies Record<string, readonly string[]>;

describe('CatalogView sections', () => {
  it.each(Object.entries(SECTIONS_BY_VIEW))(
    'draws the %s tab as one section per area',
    async (view, expected) => {
      await open();
      await router.push(`/app/catalog?view=${view}`);
      await flushPromises();

      const drawn = mounted!
        .findAll('[data-catalog]')
        .map((section) => section.attributes('data-catalog'));

      expect(drawn).toEqual(expected);
    },
  );

  it('offers one tab per view and opens the one in the address', async () => {
    await open();
    await router.push('/app/catalog?view=layouts');
    await flushPromises();

    // The Layouts tab holds its own panel toggles with the tab role, so only the page's tab row is read.
    const row = mounted!.findComponent(PageTabs);
    const selected = row.find('[role="tab"][aria-selected="true"]');
    const labels = row.findAll('[role="tab"]').map((tab) => tab.text());

    expect(labels.sort()).toEqual(Object.values(en.dashboard.catalog.tabs).sort());
    expect(selected.text()).toContain(en.dashboard.catalog.tabs.layouts);
  });

  it('shows the locked tab with its reason as the tooltip and does not open it', async () => {
    await open();
    await router.push('/app/catalog?view=locked');
    await flushPromises();

    const row = mounted!.findComponent(PageTabs);
    const locked = row.findAll('span[title]').find((wrap) => wrap.text() === en.dashboard.catalog.tabs.locked);
    const lockedTab = locked?.find('[role="tab"]');

    expect(locked?.attributes('title')).toBe(en.dashboard.catalog.lockedReason);
    expect(lockedTab?.attributes('disabled')).toBeDefined();
    expect(locked?.find('[data-slot="tab-lock"]').exists()).toBe(true);
  });
});

describe('CatalogView', () => {
  it('keeps the state and since choices after the page is opened again', async () => {
    await open();
    bar(en.dashboard.catalog.filter.state).vm.$emit('update:modelValue', 'on');
    bar(en.dashboard.catalog.filter.since).vm.$emit('update:modelValue', 'month');
    await flushPromises();
    mounted!.unmount();
    mounted = null;
    document.body.innerHTML = '';

    await open();

    const chosen = (label: string): unknown =>
      (bar(label).props() as { modelValue: unknown }).modelValue;

    expect(chosen(en.dashboard.catalog.filter.state)).toBe('on');
    expect(chosen(en.dashboard.catalog.filter.since)).toBe('month');
  });
});
