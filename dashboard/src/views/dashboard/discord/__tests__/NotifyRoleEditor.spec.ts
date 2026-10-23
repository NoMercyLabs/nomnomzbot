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

import NotifyRoleEditor from '../components/NotifyRoleEditor.vue';
import DiscordView from '../DiscordView.vue';
import en from '../i18n/en.json';

type Options = { params?: { path?: Record<string, unknown> }; body?: unknown };
type Call = {
  method: string;
  path: string;
  body: unknown;
};

const calls: Call[] = [];
const ok = (data: unknown): unknown => ({ data: { data }, response: { status: 200, ok: true } });

const storedRole = {
  id: 'role1',
  guildConnectionId: 'conn1',
  discordRoleId: 'dr1',
  roleName: 'Live pings',
  selfAssignEnabled: true,
  dmEnabled: false,
  buttonMessageId: null,
  buttonChannelId: null,
  optInCount: 4,
};

let liveRoleRows: unknown[] = [];

function answer(path: string): unknown {
  if (path.endsWith('/discord/connections'))
    return ok([
      {
        id: 'conn1',
        broadcasterId: 'c1',
        guildId: 'g1',
        guildName: 'Stoney Guild',
        botInstalled: true,
        serverConsentStatus: 'approved',
        streamerEnabled: true,
        isLinkActive: true,
      },
    ]);

  if (path.endsWith('/connections/{connectionId}/roles'))
    return ok([storedRole]);

  if (path.endsWith('/connections/{connectionId}/live-role'))
    return ok(liveRoleRows);

  return ok([]);
}

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string) => answer(path),
    PUT: async (path: string, options?: Options) => {
      calls.push({
        method: 'PUT',
        path,
        body: options?.body,
      });

      return ok(storedRole);
    },
    POST: async (path: string, options?: Options) => {
      calls.push({
        method: 'POST',
        path,
        body: options?.body,
      });

      return ok(storedRole);
    },
    DELETE: async (path: string) => {
      calls.push({
        method: 'DELETE',
        path,
        body: undefined,
      });

      return ok(null);
    },
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
  liveRoleRows = [];
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

async function editor(
  role: typeof storedRole | null,
  pickerError: Error | null = null,
): Promise<void> {
  mounted = mount(NotifyRoleEditor, {
    attachTo: document.body,
    props: {
      role: role as never,
      connectionId: 'conn1',
      pickerError,
      guildRoles: [
        {
          id: 'dr9',
          name: 'Hype',
          color: 0,
          position: 1,
          managed: false,
          mentionable: true,
          permissions: '0',
        },
      ],
      channels: [
        {
          id: 'ch1',
          name: 'roles',
          type: 0,
          position: 0,
          canPost: true,
        },
      ],
    },
    global: { plugins: plugins() },
  });

  await settle();
}

function type(selector: string, value: string): Promise<void> {
  const input = document.body.querySelector<HTMLInputElement>(selector)!;

  input.value = value;
  input.dispatchEvent(new Event('input', { bubbles: true }));

  return settle();
}

const press = (type: string): PointerEvent =>
  new PointerEvent(type, {
    bubbles: true,
    button: 0,
    pointerType: 'mouse',
  });

// reka-ui opens a select on pointerdown and picks the first option on pointerup.
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

describe('the self-assign roles panel', () => {
  it('lists each stored role with its opt-in count', async () => {
    mounted = mount(DiscordView, { attachTo: document.body, global: { plugins: plugins() } });
    await settle();

    const row = document.body.querySelector('[data-notify-role-row]');

    expect(row?.textContent).toContain('Live pings');
    expect(row?.textContent).toContain('4');
  });
});

describe('the notify role editor', () => {
  it('updates a stored role with name, self-assign and DM', async () => {
    await editor(storedRole);

    await type('[data-field="role-name"]', 'Stream alerts');
    document.body.querySelector<HTMLElement>('[data-field="dm-enabled"]')!.click();
    await settle();
    document.body.querySelector<HTMLButtonElement>('[data-save-role]')!.click();
    await settle();

    expect(calls).toHaveLength(1);
    expect(calls[0]?.method).toBe('PUT');
    expect(calls[0]?.path).toContain('/discord/roles/{roleId}');

    expect(calls[0]?.body).toEqual({
      roleName: 'Stream alerts',
      selfAssignEnabled: true,
      dmEnabled: true,
    });
  });

  it('removes a stored role only after the person confirms', async () => {
    await editor(storedRole);

    document.body.querySelector<HTMLButtonElement>('[data-remove-role]')!.click();
    await settle();

    expect(calls).toHaveLength(0);

    expect(document.body.querySelector('[data-remove-confirm]')?.textContent).toContain(
      'Remove this role?',
    );

    document.body.querySelector<HTMLButtonElement>('[data-remove-role-yes]')!.click();
    await settle();

    expect(calls).toHaveLength(1);
    expect(calls[0]?.method).toBe('DELETE');
    expect(calls[0]?.path).toContain('/discord/roles/{roleId}');
  });

  it('offers no remove for a role that does not exist yet', async () => {
    await editor(null);

    expect(document.body.querySelector('[data-remove-role]')).toBeNull();
  });

  it('leaves out the Discord role that is already the live role', async () => {
    liveRoleRows = [
      {
        id: 'live1',
        guildConnectionId: 'conn1',
        roleId: 'dr9',
        discordMemberId: '1',
        enabled: true,
        isCurrentlyApplied: false,
      },
    ];

    await editor(null);

    Element.prototype.hasPointerCapture = (): boolean => false;

    document.body
      .querySelector<HTMLElement>('[data-field="discord-role"]')
      ?.dispatchEvent(press('pointerdown'));

    await settle();

    expect(document.body.querySelectorAll('[role="option"]')).toHaveLength(0);
  });

  it('posts the opt-in button to the chosen channel', async () => {
    await editor(storedRole);

    await pickFirst('button-channel');
    document.body.querySelector<HTMLButtonElement>('[data-post-button]')!.click();
    await settle();

    expect(calls).toHaveLength(1);
    expect(calls[0]?.path).toContain('/discord/roles/{roleId}/button');
    expect(calls[0]?.body).toEqual({ buttonChannelId: 'ch1' });
  });

  it('says why under the role and the button channel picker when the bot cannot read Discord', async () => {
    await editor(storedRole, new Error('Discord read failed'));

    const fields = ['discord-role', 'button-channel'].map((field) =>
      document.body.querySelector(`[data-field="${field}"]`)?.closest('[data-slot="field"], [role="group"]'));
    const lines = fields.map((field) =>
      field?.querySelector('[data-picker-error]')?.textContent);

    expect(lines.every((line) => line?.includes('Discord read failed'))).toBe(true);
  });

  it('creates a role from the picked Discord role', async () => {
    await editor(null);

    await pickFirst('discord-role');
    document.body.querySelector<HTMLButtonElement>('[data-save-role]')!.click();
    await settle();

    expect(calls).toHaveLength(1);
    expect(calls[0]?.method).toBe('POST');
    expect(calls[0]?.path).toContain('/connections/{connectionId}/roles');

    expect(calls[0]?.body).toEqual({
      discordRoleId: 'dr9',
      roleName: 'Hype',
      selfAssignEnabled: true,
      dmEnabled: false,
    });
  });
});
