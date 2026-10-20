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
let refusePut = false;

function record(method: string, path: string, options?: Options): unknown {
  calls.push({
    method,
    path,
    body: options?.body,
  });

  if (method === 'PUT' && refusePut)
    return {
      error: { message: 'The live role is applied right now.' },
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
  refusePut = false;
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
  it('creates the rule for the picked member with the default of enabled when none is stored', async () => {
    await panel();

    expect(document.body.querySelector('[data-live-role-panel] input')).toBeNull();

    await pickFirst('live-role');
    await pickFirst('live-member');
    await click('[data-save-live-role]');

    expect(calls).toHaveLength(1);
    expect(calls[0]?.method).toBe('POST');
    expect(calls[0]?.path).toContain('/connections/{connectionId}/live-role');

    expect(calls[0]?.body).toEqual({
      roleId: 'dr9',
      discordMemberId: '5678',
      enabled: true,
    });
  });

  it('updates a stored rule with the switch turned off', async () => {
    rows = [stored];
    await panel();

    await click('[data-field="live-enabled"]');
    await click('[data-save-live-role]');

    expect(calls).toHaveLength(1);
    expect(calls[0]?.method).toBe('PUT');

    expect(calls[0]?.body).toEqual({
      roleId: 'dr9',
      discordMemberId: '1234',
      enabled: false,
    });
  });

  it('locks the role and member while the role is applied', async () => {
    rows = [{ ...stored, isCurrentlyApplied: true }];
    await panel();

    const member = document.body.querySelector<HTMLButtonElement>('[data-field="live-member"]');

    expect(member?.disabled).toBe(true);
    expect(document.body.querySelector('[data-live-applied]')).not.toBeNull();
    expect(document.body.querySelector('[data-remove-live-role]')).toBeNull();
  });

  it('shows the reason the server gives when it refuses the save', async () => {
    rows = [stored];
    refusePut = true;
    await panel();

    await click('[data-save-live-role]');

    expect(document.body.querySelector('[data-live-error]')?.textContent).toContain(
      'The live role is applied right now.',
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
