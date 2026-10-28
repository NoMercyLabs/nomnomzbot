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
import { createPinia, setActivePinia, type Pinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ref, type Plugin } from 'vue';

import { i18n } from '@/i18n';

import LiveRolePanel from '../components/LiveRolePanel.vue';
import en from '../i18n/en.json';

type Options = { body?: unknown };
type Call = {
  method: string;
  path: string;
  body: unknown;
};

const calls: Call[] = [];
const ok = (data: unknown): unknown => ({ data: { data }, response: { status: 200, ok: true } });

const stored = {
  id: 'live1',
  guildConnectionId: 'conn1',
  roleId: 'dr9',
  discordMemberId: '1234',
  enabled: true,
  isCurrentlyApplied: false,
  createdAt: '2026-10-01T00:00:00Z',
  updatedAt: '2026-10-01T00:00:00Z',
};

let rows: unknown[] = [];
let refusal: string | null = null;

function record(method: string, path: string, options?: Options): unknown {
  calls.push({
    method,
    path,
    body: options?.body,
  });

  if (method !== 'DELETE' && refusal !== null)
    return {
      error: { message: refusal },
      response: { status: 400, ok: false },
    };

  return ok(stored);
}

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async () => ok(rows),
    PUT: async (path: string, options?: Options) => record('PUT', path, options),
    POST: async (path: string, options?: Options) => record('POST', path, options),
    DELETE: async (path: string, options?: Options) => record('DELETE', path, options),
  },
}));

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({ known: ref(true), can: (): boolean => true }),
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;
let pinia: Pinia;

beforeEach(() => {
  calls.length = 0;
  rows = [];
  refusal = null;
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

const plugins = (): Array<Plugin | [Plugin, ...unknown[]]> => [
  i18n,
  pinia,
  [
    VueQueryPlugin,
    { queryClient: new QueryClient({ defaultOptions: { queries: { retry: false } } }) },
  ],
];

async function panel(): Promise<void> {
  mounted = mount(LiveRolePanel, {
    attachTo: document.body,
    props: {
      connection: {
        id: 'conn1',
        broadcasterId: 'c1',
        guildId: 'g1',
        botInstalled: true,
        serverConsentStatus: 'approved',
        approvedByDiscordUserId: '5678',
        streamerEnabled: true,
        isLinkActive: true,
        createdAt: '2026-10-01T00:00:00Z',
        updatedAt: '2026-10-01T00:00:00Z',
      },
      pickerError: null,
      guildRoles: [
        {
          id: 'dr9',
          name: 'Live now',
          color: 0,
          position: 1,
          managed: false,
          mentionable: true,
          permissions: '0',
        },
      ],
    },
    global: { plugins: plugins() },
  });

  await settle();
}

const press = (kind: string): PointerEvent =>
  new PointerEvent(kind, {
    bubbles: true,
    button: 0,
    pointerType: 'mouse',
  });

async function pickFirst(field: string): Promise<void> {
  Element.prototype.hasPointerCapture = (): boolean => false;
  Element.prototype.setPointerCapture = (): void => undefined;
  Element.prototype.releasePointerCapture = (): void => undefined;

  document.body
    .querySelector<HTMLElement>(`[data-field="${field}"]`)
    ?.dispatchEvent(press('pointerdown'));

  await settle();
  document.body.querySelector<HTMLElement>('[role="option"]')?.dispatchEvent(press('pointerup'));
  await settle();
}

const click = async (selector: string): Promise<void> => {
  document.body.querySelector<HTMLElement>(selector)!.click();
  await settle();
};

describe('the live role panel', () => {
  it('creates the rule from the role alone, with no member to pick', async () => {
    await panel();

    // Discord links the streamer's Twitch to their account, so nobody picks a member from a list.
    expect(document.body.querySelector('[data-field="live-member"]')).toBeNull();

    expect(document.body.querySelector('[data-live-owner]')?.textContent).toContain(
      'your own Discord account',
    );

    await pickFirst('live-role');
    await click('[data-save-live-role]');

    expect(calls).toHaveLength(1);
    expect(calls[0]?.method).toBe('POST');
    expect(calls[0]?.path).toContain('/connections/{connectionId}/live-role');

    expect(calls[0]?.body).toEqual({
      roleId: 'dr9',
      enabled: true,
    });
  });

  it('updates a stored rule with the switch turned off, leaving the member to the server', async () => {
    rows = [stored];
    await panel();

    await click('[data-field="live-enabled"]');
    await click('[data-save-live-role]');

    expect(calls).toHaveLength(1);
    expect(calls[0]?.method).toBe('PUT');

    expect(calls[0]?.body).toEqual({
      roleId: 'dr9',
      enabled: false,
    });
  });

  it('locks the role while the role is applied', async () => {
    rows = [{ ...stored, isCurrentlyApplied: true }];
    await panel();

    const role = document.body.querySelector<HTMLButtonElement>('[data-field="live-role"]');

    expect(role?.disabled).toBe(true);
    expect(document.body.querySelector('[data-live-applied]')).not.toBeNull();
    expect(document.body.querySelector('[data-remove-live-role]')).toBeNull();
  });

  it('shows the reason the server gives when it refuses the save', async () => {
    rows = [stored];
    refusal = 'The live role is applied right now.';
    await panel();

    await click('[data-save-live-role]');

    expect(document.body.querySelector('[data-live-error]')?.textContent).toContain(
      'The live role is applied right now.',
    );
  });

  it('tells a streamer whose Twitch is not linked on Discord what to do', async () => {
    refusal = 'In Discord, open User Settings > Connections and connect your Twitch account.';
    await panel();

    await pickFirst('live-role');
    await click('[data-save-live-role]');

    expect(document.body.querySelector('[data-live-error]')?.textContent).toContain(
      'User Settings > Connections',
    );
  });

  it('removes a stored rule', async () => {
    rows = [stored];
    await panel();

    await click('[data-remove-live-role]');

    expect(calls).toHaveLength(1);
    expect(calls[0]?.method).toBe('DELETE');
  });
});
