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
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, ref } from 'vue';
import { createMemoryHistory, createRouter } from 'vue-router';

import { i18n } from '@/i18n';

import ViewerDashboardLink from '../ViewerDashboardLink.vue';

const held = vi.hoisted(() => ({ manages: true, previewing: false }));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ current: ref(held.manages
    ? { kind: 'own' }
    : undefined) }),
}));

vi.mock('@/api/viewAs', () => ({
  useViewAs: () => ({ role: ref(held.previewing
    ? 'viewer'
    : '') }),
}));

const page = defineComponent({ render: () => null });

async function show(): Promise<VueWrapper> {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/app', component: page }],
  });
  const wrapper: VueWrapper = mount(ViewerDashboardLink, { global: { plugins: [router, i18n] } });

  await flushPromises();

  return wrapper;
}

beforeEach(() => {
  held.manages = true;
  held.previewing = false;
});

// Stoney 2026-10-29: the viewer pages are a plain place a streamer or a moderator can go to, so there must be a plain way back.
describe('the way back from the viewer pages', () => {
  it('goes to the dashboard for a person who manages a channel', async () => {
    const link = (await show()).find('a');

    expect(link.text()).toBe('Dashboard');
    expect(link.attributes('href')).toBe('/app');
  });

  it('shows nothing to a person who manages no channel, who has no dashboard to go to', async () => {
    held.manages = false;

    expect((await show()).find('a').exists()).toBe(false);
  });

  it('shows nothing while a role is previewed, where the banner has its own Exit', async () => {
    held.previewing = true;

    expect((await show()).find('a').exists()).toBe(false);
  });
});
