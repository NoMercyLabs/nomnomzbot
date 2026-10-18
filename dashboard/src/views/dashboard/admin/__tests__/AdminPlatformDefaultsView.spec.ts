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

import { queryClient } from '@/api/queryClient';
import { i18n } from '@/i18n';

import AdminPlatformDefaultsView from '../AdminPlatformDefaultsView.vue';
import en from '../i18n/en.json';
import { dashboardAdminPlatformDefaultsRoute } from '../route';

type Answer = {
  data?: unknown;
  error?: unknown;
  response: { status: number; ok: boolean };
};

type GetCall = { path: string; query: Record<string, unknown> | undefined };

const gets: GetCall[] = [];
const puts: { path: string; body: Record<string, unknown> }[] = [];
let refused: boolean = false;
let dangerous: boolean = false;
let affected: number = 12;

const action = (key: string, over: Record<string, unknown> = {}): Record<string, unknown> => ({
  actionKey: key,
  plane: 0,
  description: `${key} description`,
  shippedDefaultLevel: 4,
  platformDefaultLevel: null,
  effectiveDefaultLevel: 4,
  floorLevel: 0,
  floorTier: 2,
  channelOverrideCount: 3,
  ...over,
});

const actions = [
  action('moderation.ban'),
  action('timers.create', {
    shippedDefaultLevel: 6,
    effectiveDefaultLevel: 5,
    platformDefaultLevel: 5,
    floorLevel: 3,
  }),
];

vi.mock('vue-sonner', () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (
      path: string,
      options: { params?: { query?: Record<string, unknown> } },
    ): Promise<Answer> => {
      gets.push({ path, query: options.params?.query });

      if (refused)
        return { error: { message: 'no' }, response: { status: 403, ok: false } };

      if (path.endsWith('/blast-radius')) {
        return {
          data: {
            status: 'ok',
            data: {
              channelsAffected: affected,
              channelsKeepingOwnSetting: 3,
              sampleChannelNames: ['alpha', 'beta'],
              requiresDangerConfirmation: dangerous,
            },
          },
          response: { status: 200, ok: true },
        };
      }

      return { data: { status: 'ok', data: actions }, response: { status: 200, ok: true } };
    },
    PUT: async (
      path: string,
      options: { body: Record<string, unknown> },
    ): Promise<Answer> => {
      puts.push({ path, body: options.body });

      return { data: { status: 'ok', data: actions[0] }, response: { status: 200, ok: true } };
    },
  },
}));

let mounted: VueWrapper | null = null;
let pinia: Pinia;

beforeEach(() => {
  gets.length = 0;
  puts.length = 0;
  refused = false;
  dangerous = false;
  affected = 12;
  pinia = createPinia();
  setActivePinia(pinia);
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
  queryClient.clear();
});

const settle = async (): Promise<void> => {
  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();
};

async function open(): Promise<void> {
  mounted = mount(AdminPlatformDefaultsView, {
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
    },
  });

  await settle();
}

const row = (key: string): HTMLElement =>
  document.body.querySelector<HTMLElement>(`[data-action="${key}"]`)!;
const applyButton = (): HTMLButtonElement =>
  document.body.querySelector<HTMLButtonElement>('[data-form-action]')!;
const levelSelect = (): HTMLSelectElement =>
  document.body.querySelector<HTMLSelectElement>('[data-level]')!;
const preview = (): HTMLElement | null =>
  document.body.querySelector<HTMLElement>('[data-preview]');

async function edit(key: string): Promise<void> {
  row(key).querySelector<HTMLElement>('[data-edit]')!.click();
  await settle();
}

async function choose(value: string): Promise<void> {
  const select = levelSelect();

  select.value = value;
  select.dispatchEvent(new Event('change', { bubbles: true }));
  await settle();
}

describe('AdminPlatformDefaultsView', () => {
  it('lists every action with its role name, its source and how many channels set their own', async () => {
    await open();

    expect(document.body.querySelectorAll('[data-action]')).toHaveLength(2);
    expect(row('moderation.ban').textContent).toContain('Moderators');
    expect(row('moderation.ban').textContent).toContain('3 channels use their own');
    expect(row('moderation.ban').querySelector('[data-changed]')).toBeNull();
    expect(row('timers.create').textContent).toContain('Lead moderators');
    expect(row('timers.create').querySelector('[data-changed]')).not.toBeNull();
  });

  it('keeps Apply off until the preview of the chosen level arrived, then sends the counted number', async () => {
    await open();
    await edit('moderation.ban');
    expect(applyButton().disabled).toBe(true);

    await choose('2');

    expect(gets.at(-1)).toMatchObject({
      path: '/api/v{version}/admin/platform-defaults/actions/{actionKey}/blast-radius',
      query: { level: 2 },
    });

    expect(preview()?.textContent).toContain('12 channels');
    expect(preview()?.textContent).toContain('alpha');
    expect(applyButton().disabled).toBe(false);
    expect(applyButton().textContent).toContain('12');

    applyButton().click();
    await settle();

    expect(puts).toHaveLength(1);

    expect(puts[0]).toMatchObject({
      path: '/api/v{version}/admin/platform-defaults/actions/{actionKey}',
      body: {
        level: 2,
        confirmedChannelsAffected: 12,
        confirmDanger: false,
      },
    });
  });

  it('drops the old preview when the level changes, so Apply never confirms a stale count', async () => {
    await open();
    await edit('moderation.ban');
    await choose('2');
    expect(applyButton().disabled).toBe(false);

    affected = 40;
    await choose('3');

    expect(preview()?.textContent).toContain('40 channels');
    expect(preview()?.textContent).not.toContain('12 channels');

    applyButton().click();
    await settle();

    expect(puts[0]?.body).toMatchObject({ level: 3, confirmedChannelsAffected: 40 });
  });

  it('asks for a danger confirmation when the server says so, and sends it', async () => {
    dangerous = true;
    await open();
    await edit('moderation.ban');
    await choose('0');

    expect(document.body.querySelector('[data-confirm-danger]')).not.toBeNull();
    expect(applyButton().disabled).toBe(true);

    document.body.querySelector<HTMLElement>('[data-confirm-danger]')!.click();
    await settle();
    expect(applyButton().disabled).toBe(false);

    applyButton().click();
    await settle();

    expect(puts[0]?.body).toMatchObject({ level: 0, confirmDanger: true });
  });

  it('previews and sends a null level to go back to the shipped default', async () => {
    await open();
    await edit('timers.create');
    await choose('shipped');

    expect(gets.at(-1)?.query?.level).toBeUndefined();

    applyButton().click();
    await settle();

    expect(puts[0]?.body).toMatchObject({ level: null, confirmedChannelsAffected: 12 });
  });

  it('offers no role below the floor the server reported, plus the shipped default', async () => {
    await open();
    await edit('timers.create');

    const values = Array.from(levelSelect().options).map((option) => option.value);

    expect(values).toEqual(['shipped', '3', '4', '5', '6', '7']);
    expect(levelSelect().value).toBe('5');
  });

  it('shows the forbidden text when the server refuses the list', async () => {
    refused = true;
    await open();

    expect(document.body.textContent).toContain('You may not see the platform defaults.');
  });

  it('is an admin-only route', () => {
    expect(dashboardAdminPlatformDefaultsRoute.name).toBe('dashboard-admin-platform-defaults');
    expect(dashboardAdminPlatformDefaultsRoute.beforeEnter).toBeTypeOf('function');
  });
});
