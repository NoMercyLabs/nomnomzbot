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

  it('keeps the body scrolling unless the page is dense', async () => {
    await open({ dense: true }, false);

    expect(q('[data-slot=page-body]')?.className).not.toContain('overflow-y-auto');
  });
});

// The pages of this lane build no header of their own. The rest of the settings pages follow.
const framed = [
  '../../SettingsView.vue',
  '../../api-devices/ApiDevicesView.vue',
  '../../automod/AutomodView.vue',
  '../../banned/BannedView.vue',
  '../../billing/BillingView.vue',
  '../../blocked-terms/BlockedTermsView.vue',
  '../../channel-management/ChannelManagementView.vue',
  '../../chat-filters/ChatFiltersView.vue',
  '../../custom-event-feeds/CustomEventFeedsView.vue',
  '../../escalation/EscalationView.vue',
  '../../event-journal/EventJournalView.vue',
  '../../features/FeaturesView.vue',
] as const;

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
  it.each(framed)('%s uses SettingsPageFrame and not PageHeader', (path) => {
    const source = sources[path] ?? '';

    expect(source).toContain('<SettingsPageFrame');
    expect(source).not.toContain('<PageHeader');
    expect(source).not.toContain('<PageBody');
  });

  it('gives every framed page a route titleKey equal to its sidebar word', () => {
    const entries = settingsNav.flatMap((group) => group.entries);
    const framedNames = [
      'dashboard-settings',
      'dashboard-settings-bot-basics',
      'dashboard-settings-bot-personality',
      'dashboard-settings-engagement-triggers',
      'dashboard-settings-api-devices',
      'dashboard-settings-automod',
      'dashboard-settings-banned',
      'dashboard-settings-billing',
      'dashboard-settings-blocked-terms',
      'dashboard-settings-channel-management',
      'dashboard-settings-chat-filters',
      'dashboard-settings-custom-event-feeds',
      'dashboard-settings-escalation',
      'dashboard-settings-event-journal',
      'dashboard-settings-features',
    ];

    const wrong = framedNames.filter((name) => {
      const route = dashboardSettingsRoutes.find((item) => item.name === name);
      const entry = entries.find((item) => item.route === name);

      return route?.meta?.titleKey !== `frame.nav.${entry?.label}`;
    });

    expect(wrong).toEqual([]);
  });
});
