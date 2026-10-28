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
import { ref } from 'vue';

import { i18n } from '@/i18n';

import ObsConnectionForm from '../components/ObsConnectionForm.vue';
import en from '../i18n/en.json';

type Put = { path: string; body: unknown };

const puts: Put[] = [];

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({ known: ref(true), can: (): boolean => true }),
}));

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async () => ({ data: { data: null }, response: { status: 200, ok: true } }),
    PUT: async (path: string, init: { body?: unknown }) => {
      puts.push({ path, body: init.body });

      return { data: { data: {} }, response: { status: 200, ok: true } };
    },
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;

beforeEach(() => {
  puts.length = 0;
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

// The form is a dialog, so it renders in a portal on the body: the tests read the page, not the wrapper.
const field = (selector: string): HTMLElement => {
  const found = document.querySelector<HTMLElement>(`[role="dialog"] ${selector}`);

  if (!found)
    throw new Error(`no ${selector} in the dialog`);

  return found;
};

async function click(selector: string): Promise<void> {
  field(selector).click();
  await flushPromises();
}

async function type(selector: string, value: string): Promise<void> {
  const input = field(selector) as HTMLInputElement;

  input.value = value;
  input.dispatchEvent(new Event('input'));
  await flushPromises();
}

async function show(hasPassword: boolean): Promise<VueWrapper> {
  mounted = mount(ObsConnectionForm, {
    attachTo: document.body,
    global: { plugins: [[VueQueryPlugin, { queryClient: new QueryClient() }], i18n] },
    props: {
      open: true,
      connection: {
        mode: 'direct',
        host: 'localhost',
        port: 4455,
        hasPassword,
        hasBridgeToken: false,
        eventSubscriptionsMask: 5,
        isEnabled: true,
        lastError: 'Connection refused',
        lastConnectedAt: '2026-10-18T10:00:00Z',
        eventKinds: [
          {
            name: 'General',
            bit: 1,
            onByDefault: true,
          },
          {
            name: 'Scenes',
            bit: 4,
            onByDefault: true,
          },
          {
            name: 'Inputs',
            bit: 8,
            onByDefault: true,
          },
        ],
      },
    },
  });

  await flushPromises();

  return mounted;
}

describe('ObsConnectionForm', () => {
  it('saves the typed password and the event mask, then closes the dialog', async () => {
    const view = await show(false);

    await type('[data-obs-password]', 's3cret');
    await click('[data-kind="Inputs"]');
    await click('[data-form-action]');

    expect(puts).toHaveLength(1);
    expect(puts[0]?.path).toBe('/api/v{version}/channels/{channelId}/obs/connection');

    expect(puts[0]?.body).toEqual({
      mode: 'direct',
      host: 'localhost',
      port: 4455,
      password: 's3cret',
      eventSubscriptionsMask: 13,
      isEnabled: true,
    });

    expect(view.emitted('update:open')).toEqual([[false]]);
  });

  it('shows the last error and last connection, and saves the switch state', async () => {
    await show(false);

    expect(field('[data-obs-last-error]').textContent).toContain('Connection refused');
    expect(field('[data-obs-last-connected] time')).not.toBeNull();

    await click('[data-obs-enabled] [role="switch"]');
    await click('[data-form-action]');

    const sent = puts[0]?.body as Record<string, unknown> | undefined;

    expect(sent?.isEnabled).toBe(false);
  });

  it('keeps a stored password by sending null and never shows it', async () => {
    await show(true);
    const password = field('[data-obs-password]') as HTMLInputElement;

    expect(password.value).toBe('');
    expect(password.type).toBe('password');
    expect(field('[data-obs-password-kept]').textContent).toContain(en.dashboard.obs.connectionForm.passwordKept);

    await click('[data-form-action]');

    const sent = puts[0]?.body as Record<string, unknown> | undefined;

    expect(sent?.password).toBeNull();
  });

  it('sends an empty string when the stored password is cleared on purpose', async () => {
    await show(true);

    await click('[data-obs-password-clear]');
    await click('[data-form-action]');

    const sent = puts[0]?.body as Record<string, unknown> | undefined;

    expect(sent?.password).toBe('');
  });
});
