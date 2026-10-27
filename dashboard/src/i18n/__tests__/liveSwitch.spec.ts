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
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, h, ref } from 'vue';
import { createMemoryHistory, createRouter, RouterView, type RouteRecordRaw } from 'vue-router';

import LanguagePicker from '@/components/layout/LanguagePicker.vue';
import { Select } from '@/components/ui/select';
import { i18n, setLocale } from '@/i18n';
import { merge } from '@/lib/array';
import DashboardSidebar from '@/layouts/dashboard/DashboardSidebar.vue';
import { dashboardRoutes } from '@/router/dashboard';
import { notFoundRoute } from '@/views/site/not-found/route';

vi.mock('@/api/auth', () => ({
  useCurrentUser: () => ({ data: ref({
    displayName: 'Streamer',
    profileImageUrl: null,
    isAdmin: false,
  }) }),
  signOut: vi.fn<() => Promise<void>>(),
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({
    channels: ref([]),
    current: ref({ kind: 'own', login: null }),
    channelId: ref(null),
    isPending: ref(false),
    isLoaded: ref(true),
    choose: vi.fn<() => Promise<void>>(),
  }),
}));

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({ known: ref(false), can: () => true }),
}));

const stub = defineComponent({ render: () => h('div') });

// The real dashboard route table (so the sidebar lists its real pages) without the lazy page loaders, and the
// real not-found page, which brings its own words and the language picker of the public pages.
const pages: RouteRecordRaw[] = (dashboardRoutes.children ?? []).map(
  (child) => ({
    ...child,
    component: stub,
    path: `/app/${child.path}`,
  }) as RouteRecordRaw,
);

// The frame of the app: the real sidebar beside the page the router shows.
const Shell = defineComponent({
  render: () => h('div', null, [h(DashboardSidebar), h(RouterView)]),
});

let wrapper: VueWrapper | null = null;

function visibleWords(): string {
  return document.body.textContent ?? '';
}

async function openShell(): Promise<void> {
  const routes = merge(pages, [notFoundRoute]);
  const router = createRouter({ history: createMemoryHistory(), routes });

  await router.push('/nowhere');
  await router.isReady();

  wrapper = mount(Shell, {
    attachTo: document.body,
    global: { plugins: [i18n, router], stubs: { ChannelSwitch: stub } },
  });

  await flushPromises();
}

beforeEach(async () => {
  localStorage.clear();
  await setLocale('en');
});

afterEach(() => {
  wrapper?.unmount();
  wrapper = null;
  document.body.innerHTML = '';
});

// Stoney 2026-10-27: "changing language does not live change the language and requires a reload". Both pickers call
// chooseLocale; this is the whole path, from the picker to the words of the frame and of the open page.
describe('switching the language with the app open', () => {
  it('changes the words of the frame and of the open page without a remount', async () => {
    await openShell();

    const frameNode = wrapper!.find('[data-slot="sidebar"]').element;

    expect(visibleWords()).toContain('Music and voice');
    expect(visibleWords()).toContain('This page does not exist');

    wrapper!.findComponent(LanguagePicker).findComponent(Select).vm.$emit('update:modelValue', 'nl');
    await vi.waitFor(() => expect(i18n.global.locale.value).toBe('nl'));
    await flushPromises();

    expect(visibleWords()).toContain('Muziek en stem');
    expect(visibleWords()).toContain('Deze pagina bestaat niet');
    expect(visibleWords()).not.toContain('Music and voice');
    expect(visibleWords()).not.toContain('This page does not exist');
    expect(wrapper!.find('[data-slot="sidebar"]').element).toBe(frameNode);
    expect(document.documentElement.lang).toBe('nl');
  });

  it('changes them back when English is picked again', async () => {
    await openShell();

    await setLocale('nl');
    await flushPromises();

    expect(visibleWords()).toContain('Deze pagina bestaat niet');

    wrapper!.findComponent(LanguagePicker).findComponent(Select).vm.$emit('update:modelValue', 'en');
    await vi.waitFor(() => expect(i18n.global.locale.value).toBe('en'));
    await flushPromises();

    expect(visibleWords()).toContain('Music and voice');
    expect(visibleWords()).toContain('This page does not exist');
    expect(visibleWords()).not.toContain('Muziek en stem');
  });
});
