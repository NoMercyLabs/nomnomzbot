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
import { createMemoryHistory, createRouter } from 'vue-router';

import { i18n } from '@/i18n';
import { settingsNav } from '@/layouts/dashboard/settingsNavigation';

import { dashboardSettingsRoutes } from '../../route';
import ApiDevicesView from '../ApiDevicesView.vue';
import en from '../i18n/en.json';

type Body = Record<string, unknown>;
type Call = {
  method: string;
  path: string;
  body?: Body;
  params?: Body;
};
type Answer = {
  data?: unknown;
  error?: unknown;
  response: { status: number; ok: boolean };
};

const calls: Call[] = [];
let tokens: Body[] = [];
const held: string[] = ['automation:tokens:read', 'automation:tokens:write'];

const ok = (data: unknown): Answer => ({ data: { data }, response: { status: 200, ok: true } });
const token = (id: string, name: string, extra: Body = {}): Body => ({
  id,
  name,
  tokenPrefix: `nnz_${id}`,
  scopes: ['invoke', 'read'],
  allowedPipelineIds: [],
  lastUsedAt: null,
  expiresAt: null,
  revokedAt: null,
  createdAt: '2026-10-01T10:00:00Z',
  ...extra,
});

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string): Promise<Answer> => {
      calls.push({ method: 'GET', path });

      if (path.endsWith('/roles/effective/me'))
        return ok({ heldActionKeys: held });

      if (path.endsWith('/automation/tokens'))
        return { data: { data: tokens, hasMore: false }, response: { status: 200, ok: true } };

      if (path.endsWith('/automation/events/catalog'))
        return ok([{ publicName: 'song.changed', description: 'The song changed.' }]);

      return { data: { data: [], hasMore: false }, response: { status: 200, ok: true } };
    },
    POST: async (path: string, options: { body?: Body; params?: Body }): Promise<Answer> => {
      calls.push({
        method: 'POST',
        path,
        body: options.body,
        params: options.params,
      });

      if (path.endsWith('/rotate'))
        return ok({ secret: 'nnz_new_secret', token: token('t1', 'Deck') });

      if (path.endsWith('/automation/tokens'))
        return ok({ secret: 'nnz_made_secret', token: token('t9', String(options.body?.name)) });

      if (path.endsWith('/pair-codes'))
        return ok({ code: 'ABCD-1234', expiresAt: '2026-10-18T12:00:00Z' });

      return ok(true);
    },
    DELETE: async (path: string, options: { params?: Body }): Promise<Answer> => {
      calls.push({
        method: 'DELETE',
        path,
        params: options.params,
      });

      return ok(true);
    },
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;

beforeEach(() => {
  calls.length = 0;
  tokens = [token('t1', 'Deck'), token('t2', 'Old script', { revokedAt: '2026-09-01T00:00:00Z' })];
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

  mounted = mount(ApiDevicesView, {
    attachTo: document.body,
    global: { plugins: [i18n, [VueQueryPlugin, { queryClient: queries }]] },
  });

  await settle();
}

const q = <T extends Element>(selector: string): T => document.body.querySelector<T>(selector) as T;

async function click(el: HTMLElement): Promise<void> {
  el.click();
  await settle();
}

async function type(el: HTMLInputElement, value: string): Promise<void> {
  el.value = value;
  el.dispatchEvent(new Event('input', { bubbles: true }));
  await settle();
}

const callTo = (method: string, suffix: string): Call | undefined =>
  calls.find((call) => call.method === method && call.path.endsWith(suffix));

describe('API and devices', () => {
  it('lists each key with its secret prefix and state, and no actions on a revoked key', async () => {
    await open();

    const rows = [...document.body.querySelectorAll<HTMLElement>('[data-token-row]')];

    expect(rows.map((row) => row.dataset.state)).toEqual(['active', 'revoked']);
    expect(rows[0]?.textContent).toContain('nnz_t1');
    expect(rows[0]?.querySelector('[data-rotate]')).not.toBeNull();
    expect(rows[1]?.querySelector('[data-revoke]')).toBeNull();
  });

  it('lists the public events a key can stream', async () => {
    await open();

    expect(q('[data-event-row]').textContent).toContain('song.changed');
  });

  it('makes a key with the chosen name and scopes, then shows the secret once', async () => {
    await open();
    await click(q('[data-new-token]'));
    await type(q<HTMLInputElement>('[data-field="name"]'), 'Touch Portal');
    await click(q('[data-form-action]'));

    const sent = callTo('POST', '/automation/tokens');

    expect(sent?.body).toMatchObject({
      name: 'Touch Portal',
      scopes: ['invoke', 'read', 'events'],
      allowedPipelineIds: null,
      expiresAt: null,
    });

    expect(q('[data-secret]').textContent).toContain('nnz_made_secret');

    await click(q('[data-secret-done]'));

    expect(document.body.querySelector('[data-secret]')).toBeNull();
  });

  it('asks before it rotates, then posts the rotate and shows the new secret', async () => {
    await open();
    await click(q('[data-rotate]'));

    expect(callTo('POST', '/rotate')).toBeUndefined();

    await click(q('[data-confirm-action]'));

    expect(callTo('POST', '/rotate')?.params).toMatchObject({ path: { tokenId: 't1' } });
    expect(q('[data-secret]').textContent).toContain('nnz_new_secret');
  });

  it('asks before it revokes, then deletes that key and reads the list again', async () => {
    await open();
    await click(q('[data-revoke]'));

    expect(callTo('DELETE', '/tokens/{tokenId}')).toBeUndefined();

    const listReads = calls.filter((call) => call.path.endsWith('/automation/tokens')).length;

    await click(q('[data-confirm-action]'));

    expect(callTo('DELETE', '/tokens/{tokenId}')?.params).toMatchObject({ path: { tokenId: 't1' } });

    expect(calls.filter((call) => call.path.endsWith('/automation/tokens')).length).toBeGreaterThan(
      listReads,
    );
  });

  it('makes a pairing code for a named device and shows it', async () => {
    await open();
    await type(q<HTMLInputElement>('[data-field="device-label"]'), 'Stream Deck');
    await click(q('[data-mint]'));

    expect(callTo('POST', '/pair-codes')?.body).toEqual({ deviceLabel: 'Stream Deck', scopes: [] });
    expect(q('[data-pair-code]').textContent).toContain('ABCD-1234');
  });

  it('approves a device code the device shows', async () => {
    await open();
    await type(q<HTMLInputElement>('[data-field="user-code"]'), 'WXYZ-9876');
    await click(q('[data-approve]'));

    expect(callTo('POST', '/pair/device/approve')?.body).toEqual({ userCode: 'WXYZ-9876' });
    expect(q('[data-approved]')).not.toBeNull();
  });
});

describe('API and devices route', () => {
  it('opens the real page from the sidebar entry, not a coming-soon scaffold', async () => {
    const entry = settingsNav
      .flatMap((group) => group.entries)
      .find((item) => item.route === 'dashboard-settings-api-devices');
    const record = dashboardSettingsRoutes.find((route) => route.name === 'dashboard-settings-api-devices');
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ ...record!, path: `/app/${record!.path}` }],
    });
    const loaded = await (record!.component as () => Promise<{ default: unknown }>)();

    expect(entry).toBeDefined();
    expect(router.resolve({ name: 'dashboard-settings-api-devices' }).path).toBe('/app/settings/api-devices');
    expect(record?.meta).toEqual({ navMode: 'settings', action: 'automation:tokens:read' });
    expect(loaded.default).toBe(ApiDevicesView);
  });
});
