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
import { createPinia, setActivePinia, type Pinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ref } from 'vue';

import { i18n } from '@/i18n';

import NeedsAttentionPanel from '../components/NeedsAttentionPanel.vue';
import en from '../i18n/en.json';

type Posted = {
  path: string;
  body: unknown;
};

const posted: Posted[] = [];
let held: Record<string, unknown>[] = [];

const item = (id: string, username: string): Record<string, unknown> => ({
  id,
  kind: 'report',
  severity: 'warning',
  titleKey: 'attention_report_title',
  messageKey: 'attention_report_message',
  parameters: { username, reason: 'spam' },
  detectedAt: '2026-10-13T10:00:00Z',
  deepLinkRoute: 'people',
  sourceUserId: null,
  sourceUserName: username,
  count: 1,
  queueItemIds: [],
});

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async () => ({ data: { data: held }, response: { status: 200, ok: true } }),
    POST: async (path: string, options: { body: { ids: string[] } }) => {
      posted.push({ path, body: options.body });
      held = held.filter((entry) => !options.body.ids.includes(entry.id as string));

      return { data: { data: options.body.ids.length }, response: { status: 200, ok: true } };
    },
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;
let pinia: Pinia;

beforeEach(() => {
  posted.length = 0;
  held = [item('report:1', 'alice'), item('report:2', 'bob')];
  pinia = createPinia();
  setActivePinia(pinia);
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

const settle = async (): Promise<void> => {
  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();
};

async function openPanel(): Promise<void> {
  mounted = mount(NeedsAttentionPanel, {
    attachTo: document.body,
    global: {
      plugins: [
        i18n,
        pinia,
        [
          VueQueryPlugin,
          { queryClient: new QueryClient({ defaultOptions: { queries: { retry: false } } }) },
        ],
      ],
      stubs: { RouterLink: { template: '<a><slot /></a>' } },
    },
  });

  await settle();
}

const dismissButtons = (): HTMLElement[] =>
  [...document.body.querySelectorAll<HTMLElement>('button')].filter(
    (button) => button.textContent?.trim() === 'Dismiss',
  );

describe('NeedsAttentionPanel dismiss', () => {
  it('posts the id of the dismissed item and drops only that item from the list', async () => {
    await openPanel();

    expect(dismissButtons()).toHaveLength(2);

    dismissButtons()[0]!.click();
    await settle();

    expect(posted).toHaveLength(1);
    expect(posted[0]!.path).toContain('/notifications/action-required/dismiss');
    expect(posted[0]!.body).toEqual({ ids: ['report:1'] });
    expect(dismissButtons()).toHaveLength(1);
    expect(document.body.textContent).toContain('bob');
    expect(document.body.textContent).not.toContain('alice');
  });

  it('keeps Open as the outline button and Dismiss as the quiet ghost button', async () => {
    await openPanel();

    const variants = [...document.body.querySelectorAll<HTMLElement>('[data-variant]')]
      .slice(0, 2)
      .map((button) => button.dataset.variant);

    expect(variants).toEqual(['outline', 'ghost']);
  });
});
