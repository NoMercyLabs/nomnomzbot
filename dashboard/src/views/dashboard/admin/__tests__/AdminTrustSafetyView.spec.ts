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

import AdminTrustSafetyView from '../AdminTrustSafetyView.vue';
import en from '../i18n/en.json';
import { dashboardAdminTrustSafetyRoute } from '../route';

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
const held = vi.hoisted(() => ({
  keys: null as string[] | null,
  queueTotal: 1,
  previewFails: false,
}));

vi.mock('@/composables/usePlatformCan', () => ({
  usePlatformCan: () => ({
    known: { value: true },
    can: (key: string) => held.keys === null || held.keys.includes(key),
  }),
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

      if (path.endsWith('/review-queue'))
        return {
          data: {
            total: held.queueTotal,
            data: [
              {
                detectionId: 'd1',
                broadcasterId: 'b1',
                channelName: 'Stoney_Eagle',
                subjectDisplayName: 'SpamLord',
                reason: 'link flood',
                subjectPlatformUserId: '555',
                confidence: 'High',
                outcome: 'DeleteAndQueue',
                messageText: 'buy followers now',
                detectedAt: '2026-10-18T10:00:00Z',
                reversalPreview: 'Unbans SpamLord in Stoney_Eagle',
              },
            ],
          },
          response: { status: 200, ok: true },
        };

      if (path.endsWith('/cross-tenant-signals'))
        return {
          data: {
            data: [
              {
                provider: 'twitch',
                subjectDisplayName: 'SpamLord',
                tenantCount: 3,
                detectionCount: 7,
                hits: [],
              },
            ],
          },
          response: { status: 200, ok: true },
        };

      if (path.endsWith('/network-blocks/preview') && held.previewFails)
        return {
          error: { status: 404, detail: 'No such account' },
          response: { status: 404, ok: false },
        };

      if (path.endsWith('/network-blocks/preview'))
        return {
          data: {
            data: {
              targetDisplayName: 'SpamLord',
              tenantCount: 4,
              tenants: [],
            },
          },
          response: { status: 200, ok: true },
        };

      return {
        data: {
          data: [
            {
              id: 'blk1',
              targetDisplayName: 'BadActor',
              targetTwitchUserId: '99',
              status: 'partial',
              appliedAt: '2026-10-17T09:00:00Z',
              tenantCount: 5,
              channelCount: 5,
            },
          ],
        },
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
  },
}));

let mounted: VueWrapper | null = null;
let pinia: Pinia;

beforeEach(() => {
  calls.length = 0;
  held.keys = null;
  held.queueTotal = 1;
  held.previewFails = false;
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
  mounted = mount(AdminTrustSafetyView, {
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

async function giveReason(reason: string): Promise<void> {
  const input = document.body.querySelector<HTMLInputElement>('[data-justification]')!;

  input.value = reason;
  input.dispatchEvent(new Event('input'));
  await settle();
}

const text = (): string => document.body.textContent ?? '';
const byText = (selector: string, label: string): HTMLElement | undefined =>
  [...document.body.querySelectorAll<HTMLElement>(selector)].find(
    (node) => node.textContent?.trim() === label,
  );

describe('AdminTrustSafetyView', () => {
  it('reads nothing until the operator states a reason, then sends it with every read', async () => {
    await open();

    expect(calls).toHaveLength(0);

    await giveReason('weekly review');

    const reads = calls.filter((call) => call.method === 'GET');

    expect(reads.map((call) => call.path.split('/').pop()).sort()).toEqual([
      'cross-tenant-signals',
      'network-blocks',
      'review-queue',
    ]);

    expect(
      reads.every((call) => call.options?.params?.query?.justification === 'weekly review'),
    ).toBe(true);

    expect(document.body.querySelectorAll('[data-review-row]')).toHaveLength(1);
    expect(document.body.querySelectorAll('[data-signal-row]')).toHaveLength(1);
    expect(document.body.querySelectorAll('[data-block-row]')).toHaveLength(1);
  });

  it('shows what an overturn reverses before it posts, then posts with the reason', async () => {
    await open();
    await giveReason('weekly review');
    byText('[data-review-row] button', 'Overturn')!.click();
    await settle();

    expect(text()).toContain('Unbans SpamLord in Stoney_Eagle');
    expect(calls.some((call) => call.method === 'POST')).toBe(false);

    byText('[role="dialog"] button', 'Overturn')!.click();
    await settle();

    const post = calls.find((call) => call.method === 'POST')!;

    expect(post.path).toBe(
      '/api/v{version}/admin/trust-safety/review-queue/{detectionId}/overturn',
    );

    expect(post.options?.params?.path?.detectionId).toBe('d1');
    expect(post.options?.params?.query?.justification).toBe('weekly review');
  });

  it('names the channels a lift restores before it posts', async () => {
    await open();
    await giveReason('appeal accepted');
    byText('[data-block-row] button', 'Lift')!.click();
    await settle();

    expect(text()).toContain('5 channels');

    byText('[role="dialog"] button', 'Lift block')!.click();
    await settle();

    const post = calls.find((call) => call.method === 'POST')!;

    expect(post.path).toBe('/api/v{version}/admin/trust-safety/network-blocks/{blockId}/lift');
    expect(post.options?.params?.path?.blockId).toBe('blk1');
  });

  it('previews the blast radius and applies with exactly that confirmed count', async () => {
    await open();
    await giveReason('cross-tenant abuse');

    const target = document.body.querySelector<HTMLInputElement>('[data-block-target]')!;

    target.value = '1234';
    target.dispatchEvent(new Event('input'));
    await settle();
    byText('button', 'Preview block')!.click();
    await settle();

    expect(text()).toContain('4 channels');

    byText('button', 'Apply block')!.click();
    await settle();
    byText('[role="dialog"] button', 'Apply block')!.click();
    await settle();

    const post = calls.find((call) => call.method === 'POST')!;

    expect(post.path).toBe('/api/v{version}/admin/trust-safety/network-blocks');

    expect(post.options?.body).toMatchObject({
      targetTwitchUserId: '1234',
      justification: 'cross-tenant abuse',
      confirmedTenantCount: 4,
    });
  });

  it('hides Apply block once the target is edited after the preview', async () => {
    await open();
    await giveReason('cross-tenant abuse');

    const target = document.body.querySelector<HTMLInputElement>('[data-block-target]')!;

    target.value = '1234';
    target.dispatchEvent(new Event('input'));
    await settle();
    byText('button', 'Preview block')!.click();
    await settle();

    expect(byText('button', 'Apply block')).toBeDefined();

    target.value = '9999';
    target.dispatchEvent(new Event('input'));
    await settle();

    expect(byText('button', 'Apply block')).toBeUndefined();
    expect(text()).not.toContain('4 channels');
  });

  it('loads only the panels the admin holds a key for', async () => {
    held.keys = ['trust-safety:review'];
    await open();
    await giveReason('weekly review');

    const reviewOnly = calls
      .filter((call) => call.method === 'GET')
      .map((call) => call.path.split('/').pop());

    expect(reviewOnly).not.toContain('network-blocks');
    expect(document.body.querySelectorAll('[data-block-row]')).toHaveLength(0);
    expect(document.body.querySelector('[data-block-target]')).toBeNull();
    expect(document.body.querySelectorAll('[data-review-row]')).toHaveLength(1);

    mounted?.unmount();
    document.body.innerHTML = '';
    queryClient.clear();
    calls.length = 0;
    held.keys = ['network:block:manage'];
    await open();
    await giveReason('weekly review');

    const blocksOnly = calls
      .filter((call) => call.method === 'GET')
      .map((call) => call.path.split('/').pop());

    expect(blocksOnly).toEqual(['network-blocks']);
    expect(document.body.querySelectorAll('[data-review-row]')).toHaveLength(0);
  });

  it('opens the profile from user names and shows detection details translated', async () => {
    await open();
    await giveReason('weekly review');

    const row = document.body.querySelector('[data-review-row]')!;

    expect(row.querySelector('[data-user="555"]')).not.toBeNull();
    expect(row.textContent).toContain('High confidence');
    expect(row.textContent).toContain('Deleted and queued');
    expect(row.textContent).toContain('buy followers now');
    expect(row.textContent).not.toContain('DeleteAndQueue');

    const block = document.body.querySelector('[data-block-row]')!;

    expect(block.querySelector('[data-user="99"]')).not.toBeNull();
    expect(block.textContent).toContain('Partly applied');
  });

  it('keeps the two review actions as quiet siblings; only the network block is destructive', async () => {
    await open();
    await giveReason('weekly review');

    const overturn = byText('[data-review-row] button', 'Overturn')!;
    const confirm = byText('[data-review-row] button', 'Confirm')!;

    expect(overturn.getAttribute('data-variant')).toBe('outline');
    expect(confirm.getAttribute('data-variant')).toBe('outline');

    overturn.click();
    await settle();

    expect(byText('[role="dialog"] button', 'Overturn')!.getAttribute('data-variant')).toBe(
      'default',
    );

    byText('[role="dialog"] button', 'Cancel')!.click();
    await settle();

    const target = document.body.querySelector<HTMLInputElement>('[data-block-target]')!;

    target.value = '1234';
    target.dispatchEvent(new Event('input'));
    await settle();
    byText('button', 'Preview block')!.click();
    await settle();
    byText('button', 'Apply block')!.click();
    await settle();

    expect(byText('[role="dialog"] button', 'Apply block')!.getAttribute('data-variant')).toBe(
      'destructive',
    );
  });

  it('puts the account and the channel in separate elements so the names never glue', async () => {
    await open();
    await giveReason('weekly review');

    const line = document.body.querySelector('[data-review-row] p')!;
    const channel = line.querySelector('span.text-muted-foreground')!;

    expect(channel.textContent).toBe('Stoney_Eagle');
    expect(line.className).toContain('gap-1.5');
  });

  it('reloads the desk when the hub says another operator changed it', () => {
    const invalidate = vi.fn<(filter: { queryKey: string[] }) => void>();
    const handler = adminHandlers.find((entry) => entry.event === 'ReceiveTrustSafetyChanged')!;

    handler.handle(
      { queries: { invalidateQueries: invalidate } as unknown as QueryClient, channelId: null },
      {
        area: 'review',
        action: 'overturned',
        subjectId: 'd1',
      } as never,
    );

    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['admin', 'trust-safety'] });
  });

  it('says so when the preview fails instead of showing nothing', async () => {
    held.previewFails = true;
    await open();
    await giveReason('cross-tenant abuse');

    const target = document.body.querySelector<HTMLInputElement>('[data-block-target]')!;

    target.value = '1';
    target.dispatchEvent(new Event('input'));
    await settle();
    byText('button', 'Preview block')!.click();
    await settle();

    const alert = document.body.querySelector('[data-preview-error]');

    expect(alert?.getAttribute('role')).toBe('alert');
    expect(alert?.textContent).toContain('The preview did not load');
    expect(byText('button', 'Apply block')).toBeUndefined();

    target.value = '2';
    target.dispatchEvent(new Event('input'));
    await settle();

    expect(document.body.querySelector('[data-preview-error]')).toBeNull();
  });

  it('labels both block fields so the text never depends on the placeholder', async () => {
    await open();

    const fields = [
      document.body.querySelector<HTMLInputElement>('[data-block-target]')!,
      document.body.querySelector<HTMLInputElement>('[data-block-reason]')!,
    ];

    expect(fields.map((field) => field.labels?.[0]?.textContent?.trim())).toEqual([
      'Twitch user id',
      'Reason',
    ]);
  });

  it('scrolls the page body on a phone and stretches the left column on a desktop', async () => {
    await open();

    const body = document.body.querySelector('[data-page-body]')!;
    const grid = body.querySelector('[data-trust-grid]')!;

    expect(body.className).toContain('overflow-y-auto');
    expect(grid.className).not.toContain('items-start');
  });

  it('pages the review queue past the first 50 rows', async () => {
    held.queueTotal = 120;
    await open();
    await giveReason('weekly review');

    expect(text()).toContain('Page 1 of 3');

    byText('button', 'Next')!.click();
    await settle();

    const queueReads = calls.filter((call) => call.path.endsWith('/review-queue'));

    expect(queueReads.at(-1)?.options?.params?.query?.Page).toBe(2);
    expect(text()).toContain('Page 2 of 3');
  });

  it('shows no pager when the queue fits one page', async () => {
    await open();
    await giveReason('weekly review');

    expect(text()).not.toContain('Page 1 of');
  });

  it('registers an admin-only route', () => {
    const children = dashboardRoutes.children ?? [];
    const registered = children.find((route) => route.name === 'dashboard-admin-trust-safety');

    expect(registered?.path).toBe('admin/trust-safety');
    expect(registered?.meta?.comingSoon).toBeUndefined();
    expect(registered?.beforeEnter).toBe(dashboardAdminTrustSafetyRoute.beforeEnter);
  });
});
