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

import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ref, type Component } from 'vue';

import { fieldErrors, fieldLabels, unlinkedLabels } from '@/__tests__/fieldLinks';
import { i18n } from '@/i18n';
import CreateTokenDialog from '../api-devices/components/CreateTokenDialog.vue';
import EventFeedDialog from '../custom-event-feeds/components/EventFeedDialog.vue';
import TestFeedDialog from '../custom-event-feeds/components/TestFeedDialog.vue';
import apiDevicesEn from '../api-devices/i18n/en.json';
import feedsEn from '../custom-event-feeds/i18n/en.json';
import webhooksEn from '../webhooks/i18n/en.json';
import InboundWebhookDialog from '../webhooks/components/InboundWebhookDialog.vue';
import OutboundWebhookDialog from '../webhooks/components/OutboundWebhookDialog.vue';

vi.mock('@/api/client', () => {
  const empty = async (): Promise<unknown> => ({
    data: { data: [], hasMore: false },
    response: { status: 200, ok: true },
  });

  return { client: {
    use: () => undefined,
    GET: empty,
    POST: empty,
    PUT: empty,
    DELETE: empty,
  } };
});

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;

beforeEach(() => {
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe(): void {}
      unobserve(): void {}
      disconnect(): void {}
    },
  );

  i18n.global.mergeLocaleMessage('en', apiDevicesEn);
  i18n.global.mergeLocaleMessage('en', feedsEn);
  i18n.global.mergeLocaleMessage('en', webhooksEn);
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

async function show(dialog: Component, props: Record<string, unknown>): Promise<HTMLElement> {
  const queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  mounted = mount(dialog, {
    props: { open: true, ...props },
    attachTo: document.body,
    global: { plugins: [i18n, [VueQueryPlugin, { queryClient: queries }]] },
  });

  await settle();

  return document.body.querySelector<HTMLElement>('[role="dialog"]') as HTMLElement;
}

const forms: Array<{
  name: string;
  dialog: Component;
  props: Record<string, unknown>;
  labels: number;
}> = [
  {
    name: 'outbound webhook',
    dialog: OutboundWebhookDialog,
    props: {
      endpoint: null,
      catalog: [],
      pending: false,
    },
    labels: 4,
  },
  {
    name: 'inbound webhook',
    dialog: InboundWebhookDialog,
    props: {
      endpoint: null,
      pipelines: [],
      pending: false,
    },
    labels: 4,
  },
  {
    name: 'custom event feed',
    dialog: EventFeedDialog,
    props: {
      feed: null,
      presets: [],
      pending: false,
    },
    labels: 3,
  },
  {
    name: 'API device token',
    dialog: CreateTokenDialog,
    props: { pending: false },
    labels: 2,
  },
];

describe('Settings form fields', () => {
  for (const form of forms) {
    it(`links every label of the ${form.name} form to its control`, async () => {
      const dialog = await show(form.dialog, form.props);

      expect(fieldLabels(dialog).length).toBeGreaterThanOrEqual(form.labels);
      expect(unlinkedLabels(dialog)).toEqual([]);
    });
  }

  it('links the test-feed payload label to the payload box', async () => {
    const dialog = await show(TestFeedDialog, { feed: null });
    const labels = fieldLabels(dialog);

    expect(labels.length).toBeGreaterThanOrEqual(1);
    expect(unlinkedLabels(dialog)).toEqual([]);

    expect(dialog.querySelector(`[id="${labels[0]?.getAttribute('for')}"]`)).toBe(
      dialog.querySelector('[data-field="payload"]'),
    );
  });

  it('shows a bad header line in a FieldError under the headers box and a good note in the label FieldInfo', async () => {
    const dialog = await show(OutboundWebhookDialog, {
      endpoint: null,
      catalog: [],
      pending: false,
    });
    const headers = dialog.querySelector('[data-field="headers"]') as HTMLTextAreaElement;

    expect(fieldErrors(dialog)).toEqual([]);
    const field = headers.closest('[data-slot="field"]') as HTMLElement;

    expect(field.querySelector('[data-slot="field-description"]')).toBeNull();

    expect(field.querySelector('[data-slot="field-label"] [data-slot="field-info"]')?.textContent).toContain(
      'One header per line',
    );

    headers.value = 'no colon here';
    headers.dispatchEvent(new Event('input', { bubbles: true }));
    await settle();

    const errors = fieldErrors(dialog);

    expect(errors).toHaveLength(1);
    expect(errors[0]).toContain('no colon here');
    expect(headers.getAttribute('aria-invalid')).toBe('true');
  });
});
