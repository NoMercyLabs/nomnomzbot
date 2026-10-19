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
  adapter: 'Generic',
  ingestUrl: 'https://bot.test/webhooks/in/tok',
  verificationSecretSet: true,
  isEnabled: true,
  receiveCount: 4,
  lastReceivedAt: '2026-10-18T08:00:00Z',
  targetPipelineId: 'p1',
  targetEventType: null,
  genericConfig: {
    signatureHeaderName: 'X-Sig',
    eventKindJsonPath: '$.kind',
    providerEventIdJsonPath: '$.id',
  },
};

const deliveryRow = {
  id: 77,
  endpointId: 'o1',
  eventType: 'follow',
  attempt: 2,
  status: 'Failed',
  responseCode: 500,
  durationMs: 120,
  error: 'Server said no',
  createdAt: '2026-10-18T09:00:00Z',
};

const deadRow = {
  ...deliveryRow,
  id: 78,
  attempt: 5,
  status: 'DeadLetter',
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

        if (method === 'POST' && path.endsWith('/webhooks/outbound'))
          return {
            data: {
              status: 'success',
              data: { endpoint: outboundRow, signingSecret: 'whsec_once' },
            },
            response: { status: 200, ok: true },
          };

        if (method === 'POST' && path.endsWith('/test'))
          return {
            data: {
              status: 'success',
              data: {
                delivered: false,
                responseCode: 502,
                durationMs: 340,
                error: 'Bad gateway',
              },
            },
            response: { status: 200, ok: true },
          };

        if (method === 'POST' && path.endsWith('/rotate-secret'))
          return {
            data: {
              status: 'success',
              data: { endpoint: outboundRow, signingSecret: 'whsec_rotated' },
            },
            response: { status: 200, ok: true },
          };

        if (method !== 'GET')
          return { response: { status: 200, ok: true } };

        if (path.endsWith('/deliveries'))
          return ok([deliveryRow, deadRow]);

        if (path.endsWith('/pipelines'))
          return ok([{ id: 'p1', name: 'Thank donors' }]);

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
              category: 'Followers & Subscriptions',
            },
            {
              eventType: 'chat.message',
              label: 'Chat message',
              category: 'Chat',
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

  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe(): void {}
      unobserve(): void {}
      disconnect(): void {}
    },
  );

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

async function typeInto(selector: string, value: string): Promise<void> {
  const input = q(selector) as HTMLInputElement;

  input.value = value;
  input.dispatchEvent(new Event('input', { bubbles: true }));
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

  it('says a switched-off webhook is off with a badge, gives the reason once, and skips the never-delivered line', async () => {
    await open();

    const row = q('[data-outbound="Home server"]');

    expect(q('[data-switched-off-badge]').textContent).toContain('Switched off');
    expect(q('[data-switched-off]').textContent?.trim()).toBe('Too many failures');
    expect(row.textContent).not.toContain('repeated failures');
    expect(row.querySelector('[data-last-delivery]')).toBeNull();
  });

  it('lines the rows up with the panel header: the panel body pads 16 px', async () => {
    await open();

    for (const selector of ['[data-outbound="Home server"]', '[data-inbound="Ko-fi"]']) {
      expect(q(selector).closest('.overflow-y-auto')?.classList.contains('p-4')).toBe(true);
    }
  });

  it('draws the turn-back-on and copy-link buttons with a visible border', async () => {
    await open();

    expect(q('[data-outbound-reenable]').classList.contains('border-border')).toBe(true);
    expect(q('[data-copy-link]').classList.contains('border-border')).toBe(true);
  });

  it('keeps the icon buttons and the switch in one group, apart from the turn-back-on button', async () => {
    await open();

    const group = q('[data-outbound-delete]').closest('[data-outbound-actions]');
    const reenable = q('[data-outbound-reenable]');

    expect(group).not.toBeNull();
    expect(group?.contains(reenable)).toBe(false);

    for (const selector of [
      '[data-outbound-deliveries]',
      '[data-outbound-test]',
      '[data-outbound-rotate]',
      '[data-outbound-edit]',
      '[data-outbound-delete]',
      '[role="switch"]',
    ]) {
      expect(group?.querySelector(selector)).not.toBeNull();
    }
  });

  it('gives the inbound actions their own full-width line and the outbound icon spacing', async () => {
    await open();

    const group = q('[data-inbound-delete]').closest('[data-inbound-actions]');
    const outbound = q('[data-outbound-delete]').closest('[data-outbound-actions]');

    expect(group).not.toBeNull();
    expect(group?.parentElement?.classList.contains('w-full')).toBe(true);
    expect(group?.classList.contains('gap-1')).toBe(true);
    expect(outbound?.classList.contains('gap-1')).toBe(true);

    for (const selector of [
      '[data-copy-link]',
      '[data-inbound-edit]',
      '[data-inbound-rotate]',
      '[data-inbound-delete]',
      '[role="switch"]',
    ]) {
      expect(group?.querySelector(selector)).not.toBeNull();
    }
  });

  it('names every icon button in a tooltip on keyboard focus', async () => {
    await open();

    const buttons = {
      '[data-outbound-delete]': 'Delete Home server',
      '[data-outbound-deliveries]': 'Deliveries of Home server',
      '[data-inbound-rotate]': 'Make a new link for Ko-fi',
    } as const satisfies Record<string, string>;

    for (const [selector, label] of Object.entries(buttons)) {
      q(selector).dispatchEvent(new FocusEvent('focus'));
      await settle();

      expect(
        Array.from(document.body.querySelectorAll('[data-slot="tooltip-content"]')).some((tip) =>
          tip.textContent?.includes(label),
        ),
      ).toBe(true);

      q(selector).dispatchEvent(new FocusEvent('blur'));
      await settle();
    }
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

  it('groups the catalog events under their category heading', async () => {
    await open();
    await click(q('[data-new-outbound]'));

    const eventsOf = (category: string): string[] =>
      Array.from(
        document.querySelectorAll(`[data-event-group="${category}"] [data-event]`),
        (box) => box.getAttribute('data-event') ?? '',
      );

    expect(eventsOf('Followers & Subscriptions')).toEqual(['follow']);
    expect(eventsOf('Chat')).toEqual(['chat.message']);
  });

  it('names each event group through a resource key, not the raw backend category', async () => {
    await open();
    await click(q('[data-new-outbound]'));

    const heading = (category: string): string =>
      q(`[data-event-group="${category}"] > span`).textContent?.trim() ?? '';

    expect(heading('Followers & Subscriptions')).toBe('Twitch followers and subscribers');
    expect(heading('Chat')).toBe('Chat');
  });

  it('shows the delivery status as a worded label, not the backend string', async () => {
    await open();
    await click(q('[data-outbound-deliveries]'));

    expect(q('[data-delivery="77"]').textContent).toContain('Failed');
    expect(q('[data-delivery="78"]').textContent).toContain('Gave up');
    expect(q('[data-delivery="78"]').textContent).not.toContain('DeadLetter');
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
      bodyTemplate: null,
      bodyIsJson: true,
      customHeaders: null,
      isEnabled: true,
    });
  });

  it('sends the body template, the JSON flag and the custom headers a person fills in', async () => {
    await open();
    await click(q('[data-new-outbound]'));
    await typeInto('[data-field="name"]', 'Mine');
    await typeInto('[data-field="host"]', 'a.example.com');
    await typeInto('[data-field="body-template"]', 'a=1');
    await typeInto('[data-field="headers"]', 'X-Token: abc\nX-Env: live');
    await click(q('[data-field="body-is-json"] button[role="switch"]'));
    await click(q('[data-form-action]'));

    expect(callTo('POST', '/webhooks/outbound')?.body).toMatchObject({
      bodyTemplate: 'a=1',
      bodyIsJson: false,
      customHeaders: { 'X-Token': 'abc', 'X-Env': 'live' },
    });
  });

  it('shows the signing secret from a create once, copyable, with a warning, and drops it on close', async () => {
    await open();
    await click(q('[data-new-outbound]'));
    await typeInto('[data-field="name"]', 'Mine');
    await typeInto('[data-field="host"]', 'a.example.com');
    await click(q('[data-form-action]'));

    const reveal = q('[data-signing-secret]');

    expect(reveal.textContent).toContain('whsec_once');
    expect(document.body.textContent).toContain('only time');
    expect(q('[data-copy-secret]')).not.toBeNull();

    await click(q('[data-secret-done]'));

    expect(document.body.textContent).not.toContain('whsec_once');
  });

  it('edits the template and the flag with the PUT, and keeps stored headers when left empty', async () => {
    await open();
    await click(q('[data-outbound-edit]'));
    await typeInto('[data-field="body-template"]', 'b=2');
    await click(q('[data-form-action]'));

    expect(callTo('PUT', '/outbound/{endpointId}')?.body).toMatchObject({
      bodyTemplate: 'b=2',
      bodyIsJson: true,
      customHeaders: null,
    });
  });
});

describe('Webhooks inbound editor', () => {
  it('creates a generic inbound webhook with its signature settings and secret', async () => {
    await open();
    await click(q('[data-new-inbound]'));
    await typeInto('[data-field="inbound-name"]', 'Shop');
    await typeInto('[data-field="inbound-secret"]', 'shh');
    await typeInto('[data-field="signature-header"]', 'X-Sig');
    await typeInto('[data-field="event-kind-path"]', '$.kind');
    await typeInto('[data-field="event-id-path"]', '$.id');
    await click(q('[data-form-action]'));

    expect(callTo('POST', '/webhooks/inbound')?.body).toEqual({
      name: 'Shop',
      adapter: 'Generic',
      verificationSecret: 'shh',
      targetPipelineId: null,
      targetEventType: null,
      genericConfig: {
        signatureHeaderName: 'X-Sig',
        signaturePrefix: null,
        signingStringTemplate: null,
        timestampHeaderName: null,
        sharedSecretBodyField: null,
        eventKindJsonPath: '$.kind',
        providerEventIdJsonPath: '$.id',
      },
      isEnabled: true,
    });
  });

  it('keeps the stored secret, target and enabled flag when an edit changes only the name', async () => {
    await open();
    await click(q('[data-inbound-edit]'));
    await typeInto('[data-field="inbound-name"]', 'Shop 2');
    await click(q('[data-form-action]'));

    const body = callTo('PUT', '/inbound/{endpointId}')?.body;

    expect(body).toMatchObject({
      name: 'Shop 2',
      targetPipelineId: 'p1',
      isEnabled: true,
      genericConfig: { signatureHeaderName: 'X-Sig', eventKindJsonPath: '$.kind' },
    });

    expect(body).not.toHaveProperty('verificationSecret');
  });

  it('rotates the link only after a confirm that says the old link stops', async () => {
    await open();
    await click(q('[data-inbound-rotate]'));

    expect(callTo('POST', '/inbound/{endpointId}/rotate-token')).toBeUndefined();
    expect(document.body.textContent).toContain('old link stops');

    await click(q('[data-confirm-action]'));

    expect(callTo('POST', '/inbound/{endpointId}/rotate-token')?.params).toMatchObject({
      path: { channelId: 'c1', endpointId: 'i1' },
    });
  });
});

describe('Webhooks outbound actions', () => {
  it('sends a test delivery and shows the outcome with its code and time', async () => {
    await open();
    await click(q('[data-outbound-test]'));

    expect(callTo('POST', '/outbound/{endpointId}/test')?.params).toMatchObject({
      path: { channelId: 'c1', endpointId: 'o1' },
    });

    const result = q('[data-test-result]').textContent ?? '';

    expect(result).toContain('502');
    expect(result).toContain('340');
    expect(result).toContain('Bad gateway');
  });

  it('rotates the signing secret after a confirm and shows the new one once', async () => {
    await open();
    await click(q('[data-outbound-rotate]'));

    expect(callTo('POST', '/rotate-secret')).toBeUndefined();

    await click(q('[data-confirm-action]'));

    expect(callTo('POST', '/rotate-secret')?.params).toMatchObject({
      path: { channelId: 'c1', endpointId: 'o1' },
    });

    expect(q('[data-signing-secret]').textContent).toContain('whsec_rotated');
  });

  it('lists the deliveries of an endpoint and retries a failed one', async () => {
    await open();
    await click(q('[data-outbound-deliveries]'));

    const row = q('[data-delivery="77"]');

    expect(row.textContent).toContain('500');
    expect(row.textContent).toContain('Server said no');

    await click(q('[data-delivery-retry]'));

    expect(callTo('POST', '/deliveries/{deliveryId}/retry')?.params).toMatchObject({
      path: {
        channelId: 'c1',
        endpointId: 'o1',
        deliveryId: 77,
      },
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
      '[data-outbound-test]',
      '[data-outbound-rotate]',
      '[data-new-inbound]',
      '[data-inbound-edit]',
      '[data-inbound-rotate]',
      '[data-inbound-delete]',
      'button[role="switch"]',
    ];

    const shown = writeControls.filter(
      (selector) => document.body.querySelector(selector) !== null,
    );

    expect(shown).toEqual([]);
  });
});

describe('Webhooks deliveries permission', () => {
  it('lets a reader open the deliveries but gives them no retry', async () => {
    held.keys = new Set(['webhooks:outbound:read']);
    await open();
    await click(q('[data-outbound-deliveries]'));

    expect(q('[data-delivery="77"]')).not.toBeNull();
    expect(document.body.querySelector('[data-delivery-retry]')).toBeNull();
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
