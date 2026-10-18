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
import { adminHandlers } from '@/realtime/admin';
import { dashboardRoutes } from '@/router/dashboard';

import AdminPlatformBotView from '../AdminPlatformBotView.vue';
import en from '../i18n/en.json';
import { dashboardAdminPlatformBotRoute } from '../route';

type Options = {
  params?: { query?: Record<string, unknown>; path?: Record<string, unknown> };
  body?: unknown;
};
type Call = {
  method: string;
  path: string;
  options?: Options;
};

const calls: Call[] = [];

// The signed-in admin holds every platform key, so every action button shows.
vi.mock('@/composables/usePlatformCan', () => ({
  usePlatformCan: () => ({ known: { value: true }, can: () => true }),
}));

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string, options?: Options) => {
      calls.push({
        method: 'GET',
        path,
        options,
      });

      if (path.endsWith('/reconnect/preview'))
        return { data: { data: { affectedChannelCount: 7 } }, response: { status: 200, ok: true } };

      return {
        data: { data: { state: 'ConnectedAndWorking', botUsername: 'nomnomzbot' } },
        response: { status: 200, ok: true },
      };
    },
    POST: async (path: string, options?: Options) => {
      calls.push({
        method: 'POST',
        path,
        options,
      });

      if (path.endsWith('/device/poll'))
        return { data: { data: { status: 'pending' } }, response: { status: 200, ok: true } };

      return {
        data: {
          data: {
            deviceCode: 'dev1',
            userCode: 'ABCD-EFGH',
            verificationUri: 'https://www.twitch.tv/activate',
            interval: 5,
            expiresIn: 600,
          },
        },
        response: { status: 200, ok: true },
      };
    },
  },
}));

let mounted: VueWrapper | null = null;
let pinia: Pinia;

beforeEach(() => {
  calls.length = 0;
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
  mounted = mount(AdminPlatformBotView, {
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

const text = (): string => document.body.textContent ?? '';

async function type(selector: string, value: string): Promise<void> {
  const field = document.body.querySelector<HTMLInputElement>(selector)!;

  field.value = value;
  field.dispatchEvent(new Event('input'));
  await settle();
}

describe('AdminPlatformBotView', () => {
  it('shows the real connection state and the bot account', async () => {
    await open();

    expect(text()).toContain('Connected and working');
    expect(document.body.querySelector('[data-bot-username]')?.textContent).toContain('nomnomzbot');
  });

  it('needs a reason before it previews, then shows the counted channels', async () => {
    await open();

    const preview = document.body.querySelector<HTMLButtonElement>('[data-action-preview]')!;

    expect(preview.disabled).toBe(true);

    await type('[data-field-reason]', 'rotate the credential');
    preview.click();
    await settle();

    const call = calls.find((entry) => entry.path.endsWith('/reconnect/preview'))!;

    expect(call.options?.params?.query).toEqual({ justification: 'rotate the credential' });
    expect(document.body.querySelector('[data-preview]')?.textContent).toContain('7 channels');
  });

  it('starts the reconnect with the confirmed count and shows the device code', async () => {
    await open();
    await type('[data-field-reason]', 'rotate the credential');
    document.body.querySelector<HTMLButtonElement>('[data-action-preview]')!.click();
    await settle();
    document.body.querySelector<HTMLButtonElement>('[data-action-start]')!.click();
    await settle();

    const start = calls.find((entry) => entry.path.endsWith('/reconnect/device'))!;

    expect(start.options?.body).toEqual({
      justification: 'rotate the credential',
      confirmedAffectedChannelCount: 7,
    });

    expect(document.body.querySelector('[data-user-code]')?.textContent).toContain('ABCD-EFGH');
  });

  it('reloads the state when the hub says the platform bot changed', () => {
    const invalidate = vi.fn<(filter: { queryKey: string[] }) => void>();
    const handler = adminHandlers.find((entry) => entry.event === 'ReceivePlatformBotChanged')!;

    handler.handle(
      { queries: { invalidateQueries: invalidate } as unknown as QueryClient, channelId: null },
      { connected: true, botUsername: 'nomnomzbot' } as never,
    );

    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['admin', 'platform-bot'] });
  });

  it('registers an admin-only route', () => {
    const children = dashboardRoutes.children ?? [];
    const registered = children.find((route) => route.name === 'dashboard-admin-platform-bot');

    expect(registered?.path).toBe('admin/platform-bot');
    expect(registered?.beforeEnter).toBe(dashboardAdminPlatformBotRoute.beforeEnter);
  });
});
