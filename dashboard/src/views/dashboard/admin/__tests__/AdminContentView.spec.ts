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

import AdminContentView from '../AdminContentView.vue';
import en from '../i18n/en.json';
import { adminOnly, dashboardAdminContentRoute } from '../route';

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

const definition = {
  id: 'def1',
  kind: 'command',
  key: 'hug',
  displayName: 'Hug command',
  description: 'A warm hug',
  currentVersion: 2,
  createdAt: '2026-10-01T00:00:00Z',
};

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

      if (path.endsWith('/definitions/{id}'))
        return {
          data: {
            data: {
              definition,
              versions: [
                {
                  id: 'v2',
                  version: 2,
                  payloadJson: '{"reply":"hug"}',
                  publishedAt: '2026-10-02T00:00:00Z',
                },
                {
                  id: 'v1',
                  version: 1,
                  payloadJson: '{}',
                  publishedAt: null,
                },
              ],
              installs: {
                installedCount: 12,
                behindCount: 3,
                editedCount: 2,
              },
            },
          },
          response: { status: 200, ok: true },
        };

      return {
        data: { data: { data: [definition] } },
        response: { status: 200, ok: true },
      };
    },
    POST: async (path: string, options?: Options) => {
      calls.push({
        method: 'POST',
        path,
        options,
      });

      return { data: { data: {} }, response: { status: 200, ok: true } };
    },
    DELETE: async (path: string, options?: Options) => {
      calls.push({
        method: 'DELETE',
        path,
        options,
      });

      return { data: undefined, response: { status: 200, ok: true } };
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
  mounted = mount(AdminContentView, {
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
const byText = (selector: string, label: string): HTMLElement | undefined =>
  [...document.body.querySelectorAll<HTMLElement>(selector)].find(
    (node) => node.textContent?.trim() === label,
  );

async function type(selector: string, value: string): Promise<void> {
  const field = document.body.querySelector<HTMLInputElement>(selector)!;

  field.value = value;
  field.dispatchEvent(new Event('input'));
  await settle();
}

describe('AdminContentView', () => {
  it('lists the definitions and loads the history and install counts of the picked one', async () => {
    await open();

    expect(document.body.querySelectorAll('[data-definition-row]')).toHaveLength(1);
    expect(text()).toContain('Hug command');

    (document.body.querySelector('[data-definition-row] button') as HTMLElement).click();
    await settle();

    const detail = calls.find((call) => call.path.endsWith('/definitions/{id}'))!;

    expect(detail.options?.params?.path?.id).toBe('def1');
    expect(document.body.querySelectorAll('[data-version-row]')).toHaveLength(2);
    expect(text()).toContain('12');
  });

  it('creates a definition with the typed fields', async () => {
    await open();
    byText('button', 'New definition')!.click();
    await settle();
    await type('[data-field-kind]', 'command');
    await type('[data-field-key]', 'wave');
    await type('[data-field-name]', 'Wave command');
    await type('[data-field-payload]', '{"reply":"wave"}');
    byText('[role="dialog"] button', 'Create')!.click();
    await settle();

    const post = calls.find((call) => call.method === 'POST')!;

    expect(post.path).toBe('/api/v{version}/platform/content/definitions');

    expect(post.options?.body).toMatchObject({
      kind: 'command',
      key: 'wave',
      displayName: 'Wave command',
      payloadJson: '{"reply":"wave"}',
    });
  });

  it('refuses a payload that is not JSON and posts nothing', async () => {
    await open();
    byText('button', 'New definition')!.click();
    await settle();
    await type('[data-field-kind]', 'command');
    await type('[data-field-key]', 'wave');
    await type('[data-field-name]', 'Wave command');
    await type('[data-field-payload]', 'not json');
    byText('[role="dialog"] button', 'Create')!.click();
    await settle();

    expect(calls.some((call) => call.method === 'POST')).toBe(false);
    expect(text()).toContain('The payload must be valid JSON.');
  });

  it('drafts a new version from the current payload', async () => {
    await open();
    (document.body.querySelector('[data-definition-row] button') as HTMLElement).click();
    await settle();
    byText('button', 'Draft new version')!.click();
    await settle();

    const payload = document.body.querySelector<HTMLTextAreaElement>('[data-field-payload]')!;

    expect(payload.value).toBe('{"reply":"hug"}');

    await type('[data-field-payload]', '{"reply":"big hug"}');
    byText('[role="dialog"] button', 'Draft version')!.click();
    await settle();

    const post = calls.find((call) => call.method === 'POST')!;

    expect(post.path).toBe('/api/v{version}/platform/content/definitions/{id}/versions');
    expect(post.options?.params?.path?.id).toBe('def1');
    expect(post.options?.body).toMatchObject({ payloadJson: '{"reply":"big hug"}' });
  });

  it('shows what retiring touches before it deletes, then retires', async () => {
    await open();
    (document.body.querySelector('[data-definition-row] button') as HTMLElement).click();
    await settle();
    byText('button', 'Retire')!.click();
    await settle();

    expect(text()).toContain('12 installed copies keep running');
    expect(calls.some((call) => call.method === 'DELETE')).toBe(false);

    byText('[role="dialog"] button', 'Retire definition')!.click();
    await settle();

    const removal = calls.find((call) => call.method === 'DELETE')!;

    expect(removal.path).toBe('/api/v{version}/platform/content/definitions/{id}');
    expect(removal.options?.params?.path?.id).toBe('def1');
  });

  it('reloads the page when the hub says another operator changed the catalog', () => {
    const invalidate = vi.fn<(filter: { queryKey: string[] }) => void>();
    const handler = adminHandlers.find((entry) => entry.event === 'ReceiveContentChanged')!;

    handler.handle(
      { queries: { invalidateQueries: invalidate } as unknown as QueryClient, channelId: null },
      { action: 'retired', definitionId: 'def1' } as never,
    );

    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['admin', 'content'] });
  });

  it('registers an admin-only route', () => {
    const children = dashboardRoutes.children ?? [];
    const registered = children.find((route) => route.name === 'dashboard-admin-content');

    expect(registered?.path).toBe('admin/content');
    expect(registered?.beforeEnter).toBe(adminOnly);
    expect(registered?.component).toBe(dashboardAdminContentRoute.component);
    expect(registered?.meta?.comingSoon).toBeUndefined();
  });
});
