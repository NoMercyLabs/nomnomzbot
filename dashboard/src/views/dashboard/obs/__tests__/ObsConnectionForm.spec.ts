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

function show(hasPassword: boolean): VueWrapper {
  mounted = mount(ObsConnectionForm, {
    attachTo: document.body,
    global: { plugins: [[VueQueryPlugin, { queryClient: new QueryClient() }], i18n] },
    props: {
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

  return mounted;
}

describe('ObsConnectionForm', () => {
  it('saves the typed password and the event mask, and clears the password field after', async () => {
    const view = show(false);

    await view.get('[data-obs-password]').setValue('s3cret');
    await view.get('[data-kind="Inputs"]').trigger('click');
    await view.get('[data-obs-save]').trigger('click');
    await flushPromises();

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

    expect((view.get('[data-obs-password]').element as HTMLInputElement).value).toBe('');
  });

  it('shows the last error and last connection, and saves the switch state', async () => {
    const view = show(false);

    expect(view.get('[data-obs-last-error]').text()).toContain('Connection refused');
    expect(view.find('[data-obs-last-connected] time').exists()).toBe(true);

    await view.get('[data-obs-enabled]').trigger('click');
    await view.get('[data-obs-save]').trigger('click');
    await flushPromises();

    const sent = puts[0]?.body as Record<string, unknown> | undefined;

    expect(sent?.isEnabled).toBe(false);
  });

  it('keeps a stored password by sending null and never shows it', async () => {
    const view = show(true);
    const field = view.get('[data-obs-password]').element as HTMLInputElement;

    expect(field.value).toBe('');
    expect(field.type).toBe('password');
    expect(view.text()).toContain(en.dashboard.obs.connectionForm.passwordKept);

    await view.get('[data-obs-save]').trigger('click');
    await flushPromises();

    const sent = puts[0]?.body as Record<string, unknown> | undefined;

    expect(sent?.password).toBeNull();
  });

  it('sends an empty string when the stored password is cleared on purpose', async () => {
    const view = show(true);

    await view.get('[data-obs-password-clear]').trigger('click');
    await view.get('[data-obs-save]').trigger('click');
    await flushPromises();

    const sent = puts[0]?.body as Record<string, unknown> | undefined;

    expect(sent?.password).toBe('');
  });
});
