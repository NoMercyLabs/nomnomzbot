// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ref } from 'vue';

import { i18n } from '@/i18n';
import { dashboardSettingsRoutes } from '../../route';
import en from '../i18n/en.json';
import WebhooksView from '../WebhooksView.vue';

type Body = Record<string, unknown>;
type Call = {
  method: string;
  path: string;
  body?: Body;
  params?: Body;
};

const calls: Call[] = [];

const held = vi.hoisted(() => ({ keys: null as Set<string> | null }));

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({
    known: ref(true),
    can: (key: string) => held.keys === null || held.keys.has(key),
  }),
}));

const ok = (data: unknown): { data: unknown; response: { status: number; ok: boolean } } => ({
  data: { data, hasMore: false },
  response: { status: 200, ok: true },
});

const outboundRow = {
  id: 'o1',
  name: 'Home server',
  fqdn: 'hooks.example.com',
  path: '/in',
  subscribedEventTypes: ['follow'],
  bodyIsJson: true,
  isEnabled: false,
  consecutiveFailureCount: 5,
  disabledAt: '2026-10-18T09:00:00Z',
  disabledReason: 'Too many failures',
};

const inboundRow = {
  id: 'i1',
  name: 'Ko-fi',
  adapter: 'generic',
  ingestUrl: 'https://bot.test/webhooks/in/tok',
  verificationSecretSet: true,
  isEnabled: true,
  receiveCount: 4,
  lastReceivedAt: '2026-10-18T08:00:00Z',
  genericConfig: null,
};

vi.mock('@/api/client', () => {
  const record =
    (method: string) =>
      async (path: string, options: { body?: Body; params?: Body } = {}) => {
        calls.push({
          method,
          path,
          body: options.body,
          params: options.params,
        });

        if (method !== 'GET')
          return { response: { status: 200, ok: true } };

        if (path.endsWith('/blast-radius')) {
          return ok({
            categories: [
              {
                categoryKey: 'blast_radius_category_pipeline_steps',
                count: 2,
                sample: [],
              },
              {
                categoryKey: 'blast_radius_category_pending_webhook_deliveries',
                count: 3,
                sample: [],
              },
            ],
            isMinimum: false,
          });
        }

        // cspell:disable-next-line -- the route path carries the British spelling
        if (path.endsWith('/event-catalogue'))
          return ok([
            {
              eventType: 'follow',
              label: 'Follow',
              category: 'channel',
            },
          ]);

        return path.endsWith('/inbound')
          ? ok([inboundRow])
          : ok([outboundRow]);
      };

  return {
    client: {
      use: () => undefined,
      GET: record('GET'),
      POST: record('POST'),
      PUT: record('PUT'),
      DELETE: record('DELETE'),
    },
  };
});

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;

beforeEach(() => {
  calls.length = 0;
  held.keys = null;
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

async function settle(): Promise<void> {
  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();
}

async function open(): Promise<void> {
  const queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  mounted = mount(WebhooksView, {
    attachTo: document.body,
    global: { plugins: [i18n, [VueQueryPlugin, { queryClient: queries }]] },
  });

  await settle();
}

const q = (selector: string): HTMLElement =>
  document.body.querySelector<HTMLElement>(selector) as HTMLElement;

async function click(el: HTMLElement): Promise<void> {
  el.click();
  await settle();
}

const callTo = (method: string, suffix: string): Call | undefined =>
  calls.find((call) => call.method === method && call.path.endsWith(suffix));

describe('Webhooks page', () => {
  it('lists both kinds and says why an outbound webhook is switched off', async () => {
    await open();

    expect(q('[data-outbound="Home server"]').textContent).toContain('hooks.example.com/in');
    expect(q('[data-switched-off]').textContent).toContain('Too many failures');
    expect(q('[data-inbound="Ko-fi"]').textContent).toContain('4');
  });

  it('turns a switched-off outbound webhook back on with the re-enable call', async () => {
    await open();
    await click(q('[data-outbound-reenable]'));

    expect(callTo('POST', '/outbound/{endpointId}/reenable')?.params).toMatchObject({
      path: { channelId: 'c1', endpointId: 'o1' },
    });
  });

  it('flips an inbound webhook with a PUT that carries only the enabled flag', async () => {
    await open();
    await click(q('[data-inbound="Ko-fi"] button[role="switch"]'));

    expect(callTo('PUT', '/inbound/{endpointId}')?.body).toEqual({ isEnabled: false });
  });

  it('counts what an inbound webhook reaches before the confirm, and deletes only after it', async () => {
    await open();
    await click(q('[data-inbound-delete]'));

    expect(callTo('GET', '/blast-radius')).toBeDefined();
    expect(callTo('DELETE', '/inbound/{endpointId}')).toBeUndefined();
    expect(document.body.textContent).toContain('2');

    await click(q('[data-confirm-action]'));

    expect(callTo('DELETE', '/inbound/{endpointId}')?.params).toMatchObject({
      path: { channelId: 'c1', endpointId: 'i1' },
    });
  });

  it('counts what an outbound webhook reaches before the confirm, and deletes only after it', async () => {
    await open();
    await click(q('[data-outbound-delete]'));

    expect(callTo('GET', '/outbound/{endpointId}/blast-radius')?.params).toMatchObject({
      path: { channelId: 'c1', endpointId: 'o1' },
    });

    expect(callTo('DELETE', '/outbound/{endpointId}')).toBeUndefined();
    expect(document.body.textContent).toContain('Pipeline steps that use it: 2');
    expect(document.body.textContent).toContain('Deliveries still waiting to be sent: 3');

    await click(q('[data-confirm-action]'));

    expect(callTo('DELETE', '/outbound/{endpointId}')?.params).toMatchObject({
      path: { channelId: 'c1', endpointId: 'o1' },
    });
  });

  it('creates an outbound webhook with the ticked catalog events', async () => {
    await open();
    await click(q('[data-new-outbound]'));

    const type = async (selector: string, value: string): Promise<void> => {
      const input = q(selector) as HTMLInputElement;

      input.value = value;
      input.dispatchEvent(new Event('input', { bubbles: true }));
      await settle();
    };

    await type('[data-field="name"]', 'Mine');
    await type('[data-field="host"]', 'a.example.com');
    await click(q('[data-event="follow"]'));
    await click(q('[data-form-action]'));

    expect(callTo('POST', '/webhooks/outbound')?.body).toEqual({
      name: 'Mine',
      fqdn: 'a.example.com',
      path: null,
      subscribedEventTypes: ['follow'],
      isEnabled: true,
    });
  });
});

describe('Webhooks permissions', () => {
  it('shows the state but none of the write controls to a person who holds no webhook key', async () => {
    held.keys = new Set();
    await open();

    expect(q('[data-outbound="Home server"]')).not.toBeNull();
    expect(q('[data-inbound="Ko-fi"]')).not.toBeNull();

    const writeControls = [
      '[data-new-outbound]',
      '[data-outbound-reenable]',
      '[data-inbound-delete]',
      'button[role="switch"]',
    ];

    const shown = writeControls.filter(
      (selector) => document.body.querySelector(selector) !== null,
    );

    expect(shown).toEqual([]);
  });
});

describe('Webhooks route', () => {
  it('carries the action key and loads the real page', async () => {
    const record = dashboardSettingsRoutes.find(
      (route) => route.name === 'dashboard-settings-webhooks',
    );
    const loaded = await (record!.component as () => Promise<{ default: unknown }>)();

    expect(record?.meta).toEqual({ navMode: 'settings', action: 'webhooks:outbound:read' });
    expect(loaded.default).toBe(WebhooksView);
  });
});
