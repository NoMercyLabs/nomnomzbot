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

import { createMemoryHistory, createRouter } from 'vue-router';

import { SAVE_FAILED_MESSAGE, SAVE_FAILED_TITLE } from '@/composables/useOptimisticSave';
import { i18n } from '@/i18n';
import { useSaveErrorsStore } from '@/stores/saveErrors';

import ErrorInbox from '../ErrorInbox.vue';

type Posted = {
  path: string;
  body: unknown;
};

const posted: Posted[] = [];
let held: Record<string, unknown>[] = [];

const item = (id: string, username: string) => ({
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
}) as const satisfies Record<string, unknown>;

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

const page = { template: '<div />' };
const router = createRouter({
  history: createMemoryHistory(),
  routes: [
    {
      path: '/app/chat',
      name: 'dashboard-chat',
      component: page,
    },
    {
      path: '/app/u/:channelName/chat',
      name: 'u:dashboard-chat',
      component: page,
    },
    {
      path: '/app/settings/webhooks',
      name: 'dashboard-settings-webhooks',
      component: page,
    },
    {
      path: '/app/pipelines/:id',
      name: 'dashboard-pipeline',
      component: page,
    },
    { path: '/:rest(.*)', component: page },
  ],
});

let mounted: VueWrapper | null = null;
let pinia: Pinia;

beforeEach(() => {
  posted.length = 0;
  held = [item('report:1', 'alice'), item('report:2', 'bob')];
  pinia = createPinia();
  setActivePinia(pinia);
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

async function openInbox(): Promise<void> {
  mounted = mount(ErrorInbox, {
    attachTo: document.body,
    global: {
      plugins: [
        i18n,
        pinia,
        router,
        [
          VueQueryPlugin,
          { queryClient: new QueryClient({ defaultOptions: { queries: { retry: false } } }) },
        ],
      ],
    },
  });

  await settle();
  document.body.querySelector<HTMLElement>('[data-error-inbox]')?.click();
  await settle();
}

const rows = (): HTMLElement[] => [...document.body.querySelectorAll<HTMLElement>('li')];
const dismissIn = (row: HTMLElement): HTMLElement =>
  [...row.querySelectorAll<HTMLElement>('button')].find(
    (button) => button.textContent?.trim() === 'Dismiss',
  ) as HTMLElement;

// Stoney 2026-10-14: "it does not tell me what and where so this is kinda useless".
describe('a failed save in the inbox', () => {
  it('names the change, the page it was made on, and leads back there', async () => {
    held = [];

    useSaveErrorsStore().report(
      SAVE_FAILED_TITLE,
      SAVE_FAILED_MESSAGE,
      { key: 'unstar', params: { name: ':raised_hand:' } },
      '/app/chat',
    );

    router.push('/somewhere');
    await openInbox();

    const row = rows()[0]!;

    expect(row.querySelector('[data-save-what]')?.textContent).toBe('Not saved: Remove the star from :raised_hand:');
    expect(row.querySelector('[data-save-where]')?.textContent).toBe('On Chat');
    expect(row.querySelector('a')?.getAttribute('href')).toBe('/app/chat');
  });
});

// A page the main sidebar does not list (a channel-prefixed address, a settings page, a pipeline) still gets a plain name.
describe('the page a failed save names', () => {
  it.each([
    ['/app/u/someone/chat', 'On Chat'],
    ['/app/settings/webhooks', 'On Webhooks'],
    ['/app/pipelines/p1', 'On Pipelines'],
  ])('shows a plain name for %s', async (where, words) => {
    held = [];

    useSaveErrorsStore().report(SAVE_FAILED_TITLE, SAVE_FAILED_MESSAGE, { key: 'star', params: { name: 'a' } }, where);

    await openInbox();

    expect(rows()[0]!.querySelector('[data-save-where]')?.textContent).toBe(words);
  });
});

// Stoney 2026-10-21 (mobile ledger M-06): "Action music_toggle_saved failed" showed twice in the inbox.
describe('a failed save that repeats', () => {
  it('is listed once with a count, and one dismiss clears it', async () => {
    held = [];

    const what = { key: 'star', params: { name: 'a' } };

    useSaveErrorsStore().report(SAVE_FAILED_TITLE, SAVE_FAILED_MESSAGE, what, '/app/chat');
    useSaveErrorsStore().report(SAVE_FAILED_TITLE, SAVE_FAILED_MESSAGE, what, '/app/chat');
    useSaveErrorsStore().report(SAVE_FAILED_TITLE, SAVE_FAILED_MESSAGE, { key: 'star', params: { name: 'b' } }, '/app/chat');
    await openInbox();

    expect(rows()).toHaveLength(2);
    expect(rows()[0]!.querySelector('[data-save-count]')?.textContent?.trim()).toBe('2');
    expect(rows()[1]!.querySelector('[data-save-count]')).toBeNull();
    expect(document.body.querySelector('[data-error-inbox]')?.textContent?.trim()).toBe('2');

    dismissIn(rows()[0]!).click();
    await settle();

    expect(rows()).toHaveLength(1);
    expect(useSaveErrorsStore().items).toHaveLength(1);
  });
});

// Stoney 2026-10-20: when OBS shuts down, one notice says OBS is not connected, with a way to go there.
describe('the OBS not connected notice', () => {
  const obsDown = {
    id: 'owner-action-failed:obs-not-connected',
    kind: 'obs_not_connected',
    severity: 'warning',
    titleKey: 'attention_obs_not_connected_title',
    messageKey: 'attention_obs_not_connected_message',
    parameters: { count: '3' },
    detectedAt: '2026-10-20T10:00:00Z',
    deepLinkRoute: 'obs',
    sourceUserId: null,
    sourceUserName: null,
    count: 3,
    queueItemIds: [],
  } as const satisfies Record<string, unknown>;

  it('is one item that says how many actions could not run and opens the OBS page', async () => {
    held = [obsDown];
    await openInbox();

    const row = rows()[0]!;

    expect(rows()).toHaveLength(1);
    expect(row.textContent).toContain('OBS is not connected');
    expect(row.textContent).toContain('Actions that could not run because OBS was not connected: 3');
    expect(row.querySelector('a')?.textContent?.trim()).toBe('Open OBS');
    expect(row.querySelector('a')?.getAttribute('href')).toBe('/app/obs');
  });
});

describe('ErrorInbox button size', () => {
  it('is a 36px button with or without a count', async () => {
    await openInbox();
    const withCount = document.body.querySelector('[data-error-inbox]')!;

    expect(withCount.className).toContain('h-9');
    expect(withCount.className).not.toContain('h-8');
  });
});

describe('ErrorInbox dismiss', () => {
  it('posts the id of the dismissed item and drops only that item from the list', async () => {
    await openInbox();

    expect(rows()).toHaveLength(2);

    dismissIn(rows()[0]!).click();
    await settle();

    expect(posted).toHaveLength(1);
    expect(posted[0]!.path).toContain('/notifications/action-required/dismiss');
    expect(posted[0]!.body).toEqual({ ids: ['report:1'] });
    expect(rows()).toHaveLength(1);
    expect(rows()[0]!.textContent).toContain('bob');
    expect(rows()[0]!.textContent).not.toContain('alice');
  });

  it('keeps Open as the outline link and Dismiss as the quiet ghost button', async () => {
    await openInbox();

    const buttons = [...rows()[0]!.querySelectorAll<HTMLElement>('[data-variant]')];

    expect(buttons.map((button) => button.dataset.variant)).toEqual(['outline', 'ghost']);
  });
});
