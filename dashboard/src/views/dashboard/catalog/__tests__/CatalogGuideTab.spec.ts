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

async function openGuide(): Promise<void> {
  const blank = { template: '<div />' };

  router = createRouter({
    history: createMemoryHistory(),
    routes: [{
      path: '/app/catalog',
      name: 'dashboard-catalog',
      component: blank,
    }],
  });

  await router.push('/app/catalog?view=guide');

  mounted = mount(CatalogView, {
    attachTo: document.body,
    global: {
      plugins: [i18n, pinia, router],
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

describe('the catalog guide tab', () => {
  it('offers a Guide tab and opens it from the address', async () => {
    await openGuide();

    const row = mounted!.findComponent(PageTabs);
    const selected = row.find('[role="tab"][aria-selected="true"]');

    expect(selected.text()).toContain(en.dashboard.catalog.tabs.guide);
  });

  it('draws the tiers, the action placement and the advanced filter recipe as sections', async () => {
    await openGuide();

    const drawn = mounted!
      .findAll('[data-catalog]')
      .map((section) => section.attributes('data-catalog'));

    expect(drawn).toEqual(['guide-tiers', 'guide-actions', 'guide-filters']);
  });
});
