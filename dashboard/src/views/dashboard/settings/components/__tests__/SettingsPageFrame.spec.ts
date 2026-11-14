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
import { afterEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, h } from 'vue';
import { createMemoryHistory, createRouter } from 'vue-router';

import { i18n } from '@/i18n';
import { settingsNav } from '@/layouts/dashboard/settingsNavigation';

import { dashboardSettingsRoutes } from '../../route';
import SettingsPageFrame from '../SettingsPageFrame.vue';

let mounted: VueWrapper | null = null;

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

const blank = defineComponent({ render: () => h('div') });

async function open(
  props: Record<string, unknown>,
  withActions: boolean,
  extraSlots: Record<string, () => ReturnType<typeof h>> = {},
): Promise<void> {
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe(): void {}
      unobserve(): void {}
      disconnect(): void {}
    },
  );

  i18n.global.mergeLocaleMessage('en', {
    frame: { nav: { settingsPages: { billing: 'Billing' } } },
    dashboard: { settings: { crumb: 'Setup' } },
  });

  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/app/settings/billing',
        name: 'billing',
        meta: { titleKey: 'frame.nav.settingsPages.billing' },
        component: blank,
      },
    ],
  });

  await router.push('/app/settings/billing');

  const slots = {
    default: () => h('p', { 'data-test': 'content' }, 'Body content'),
    ...(withActions
      ? { actions: () => h('button', { 'data-test': 'save' }, 'Save') }
      : {}),
    ...extraSlots,
  };

  mounted = mount(SettingsPageFrame, {
    attachTo: document.body,
    props,
    slots,
    global: { plugins: [i18n, router] },
  });

  await flushPromises();
}

const q = (selector: string): HTMLElement | null => document.body.querySelector(selector);

describe('SettingsPageFrame', () => {
  it('shows the translated title from the route and the Settings crumb', async () => {
    await open({}, false);

    expect(q('h1')?.textContent).toBe('Billing');
    expect(q('[data-slot=page-crumb]')?.textContent?.trim()).toBe('Setup');
  });

  it('puts the actions slot in the header actions and the content in the page body', async () => {
    await open({}, true);

    expect(q('[data-slot=page-actions] [data-test=save]')?.textContent).toBe('Save');
    expect(q('[data-slot=page-body] [data-test=content]')?.textContent).toBe('Body content');
    expect(q('header [data-test=content]')).toBeNull();
  });

  it('lets a page set its own title and crumb', async () => {
    await open({ title: 'Live title', crumb: 'Moderation' }, false);

    expect(q('h1')?.textContent).toBe('Live title');
    expect(q('[data-slot=page-crumb]')?.textContent?.trim()).toBe('Moderation');
  });

  it('draws no body of its own when body is none', async () => {
    await open({ body: 'none' }, false);

    expect(q('[data-slot=page-body]')).toBeNull();
    expect(q('[data-test=content]')?.textContent).toBe('Body content');
  });

  it('puts the tabs slot in the header bar, above the body', async () => {
    await open({}, false, { tabs: () => h('nav', { 'data-test': 'tabs' }, 'Tabs') });

    expect(q('header [data-slot=page-bar] [data-test=tabs]')?.textContent).toBe('Tabs');
    expect(q('[data-slot=page-body] [data-test=tabs]')).toBeNull();
  });

  it('puts the panel slot beside the page body, in the one panel-beside row', async () => {
    await open({}, false, { panel: () => h('aside', { 'data-test': 'panel' }, 'Side') });

    expect(q('[data-slot=panel-beside] [data-slot=page-body] [data-test=content]')?.textContent).toBe('Body content');
    expect(q('[data-slot=panel-beside] [data-test=panel]')?.textContent).toBe('Side');
  });

  it('draws no panel row when the page has no panel slot', async () => {
    await open({}, false);

    expect(q('[data-slot=panel-beside]')).toBeNull();
  });

  it('keeps the body scrolling unless the page is dense', async () => {
    await open({ dense: true }, false);

    expect(q('[data-slot=page-body]')?.className).not.toContain('overflow-y-auto');
  });
});

// Every settings page builds no header of its own. The glob reads each page, so a new page is
// guarded the moment it exists. The views inside a page's components folder render inside it.
const sources: Record<string, string> = import.meta.glob(
  [
    '../../SettingsView.vue',
    '../../*/*View.vue',
  ],
  {
    eager: true,
    query: '?raw',
    import: 'default',
  },
);

describe('the settings pages on the frame', () => {
  it('reads the settings page itself and its 22 pages', () => {
    expect(Object.keys(sources)).toHaveLength(23);
  });

  it.each(Object.keys(sources))('%s uses SettingsPageFrame and not PageHeader', (path) => {
    const source = sources[path] ?? '';

    expect(source).toContain('<SettingsPageFrame');
    expect(source).not.toContain('<PageHeader');
    expect(source).not.toMatch(/import\s+PageHeader\b/);

    // A page that sets its own body (a list with a detail, a panel beside) draws it in the frame.
    const setsOwnBody = /\sbody=|:body=/.test(source);
    const ownBodyTags = setsOwnBody
      ? 0
      : source.split('<PageBody').length - 1;

    expect(ownBodyTags).toBe(0);
  });

  it('gives every framed page a route titleKey equal to its sidebar word', () => {
    const entries = settingsNav.flatMap((group) => group.entries);
    // Supporter Sources reuses the page of another area, which draws its own header.
    const reused = 'dashboard-settings-supporter-sources';
    const framed = dashboardSettingsRoutes.filter((route) => route.name !== reused);

    const wrong = framed.filter((route) => {
      const entry = entries.find((item) => item.route === route.name);

      return route.meta?.titleKey !== `frame.nav.${entry?.label}`;
    });

    expect(framed).toHaveLength(26);

    expect(wrong).toEqual([]);
  });
});
