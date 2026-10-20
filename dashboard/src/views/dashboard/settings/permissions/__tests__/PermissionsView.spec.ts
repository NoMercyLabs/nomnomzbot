// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ref } from 'vue';
import { createMemoryHistory, createRouter, type Router } from 'vue-router';

import { i18n } from '@/i18n';
import { settingsNav } from '@/layouts/dashboard/settingsNavigation';

import { dashboardSettingsRoutes } from '../../route';
import OptionSelect, { type Option } from '../components/OptionSelect.vue';
import { actionNameSlug } from '../actionArea';
import en from '../i18n/en.json';
import nl from '../i18n/nl.json';
import PermissionsView from '../PermissionsView.vue';

type Body = Record<string, unknown>;
type Call = {
  method: string;
  path: string;
  body?: Body;
  params?: Body;
};
type Answer = {
  data?: unknown;
  response: { status: number; ok: boolean };
};

const calls: Call[] = [];

const page = (data: unknown): Answer => ({
  data: { data, hasMore: false },
  response: { status: 200, ok: true },
});

const members = [
  {
    id: 'm1',
    userId: 'u-mod',
    username: 'modmary',
    role: 0,
    isOwner: false,
  },
  {
    id: 'm2',
    userId: 'u-owner',
    username: 'ownerolly',
    role: 3,
    isOwner: true,
  },
  {
    id: 'm3',
    userId: 'u-stale',
    username: 'stalesam',
    role: 3,
    isOwner: false,
  },
];

const matrix = [
  {
    actionKey: 'chat.timeout',
    plane: 1,
    description: 'Time a viewer out',
    floorLevel: 10,
    floorTier: 1,
    isGrantableViaPermit: false,
    overrideLevel: 20,
    effectiveLevel: 20,
  },
  {
    actionKey: 'chat.permit-link',
    plane: 0,
    description: 'Allow one link',
    floorLevel: 0,
    floorTier: 2,
    isGrantableViaPermit: true,
    overrideLevel: null,
    effectiveLevel: 10,
  },
  {
    actionKey: 'obs:control',
    plane: 1,
    description: 'Control OBS',
    floorLevel: 30,
    floorTier: 3,
    isGrantableViaPermit: true,
    overrideLevel: null,
    effectiveLevel: 30,
  },
  {
    actionKey: 'bot:manager:author',
    plane: 1,
    description: 'Bot manager',
    floorLevel: 40,
    floorTier: 3,
    isGrantableViaPermit: true,
    overrideLevel: null,
    effectiveLevel: 40,
  },
  {
    actionKey: 'chat:unknown',
    plane: 0,
    description: null,
    floorLevel: 0,
    floorTier: 2,
    isGrantableViaPermit: false,
    overrideLevel: null,
    effectiveLevel: 0,
  },
];

const vipGrant = {
  id: 1,
  subjectType: 'user',
  subjectId: 'u-vip',
  subjectName: 'vipvic',
  resourceType: 'action',
  resourceId: 'chat.permit-link',
  permissionValue: 'allow',
};

// Each test starts from the one grant above; a revoke in an earlier test does not carry over.
const grants: Record<string, unknown>[] = [];

const found = [
  {
    id: 'u-new',
    label: 'newbie',
    subLabel: 'seen in chat',
    inChannel: true,
  },
];

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({ known: ref(true), can: () => true }),
}));

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string): Promise<Answer> => {
      calls.push({ method: 'GET', path });

      if (path.endsWith('/action-permissions'))
        return page(matrix);

      if (path.endsWith('/permissions'))
        return page(structuredClone(grants));

      if (path.endsWith('/community/search'))
        return page(found);

      return page(members);
    },
    POST: async (path: string, options: { body?: Body; params?: Body }): Promise<Answer> => {
      calls.push({
        method: 'POST',
        path,
        body: options.body,
        params: options.params,
      });

      if (path.endsWith('/permissions')) {
        grants.push({
          id: grants.length + 1,
          subjectType: 'user',
          subjectId: options.body?.userId,
          subjectName: 'newbie',
          resourceType: 'action',
          resourceId: options.body?.permission,
          permissionValue: 'allow',
        });
      }

      return { response: { status: 200, ok: true } };
    },
    PUT: async (path: string, options: { body?: Body; params?: Body }): Promise<Answer> => {
      calls.push({
        method: 'PUT',
        path,
        body: options.body,
        params: options.params,
      });

      return { response: { status: 200, ok: true } };
    },
    DELETE: async (path: string, options: { params?: Body }): Promise<Answer> => {
      calls.push({
        method: 'DELETE',
        path,
        params: options.params,
      });

      const query = options.params?.query as Body | undefined;
      const at = grants.findIndex((row) => row.resourceId === query?.permission);

      if (path.endsWith('/permissions/{userId}') && at >= 0) {
        grants.splice(at, 1);
      }

      return { response: { status: 204, ok: true } };
    },
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;

beforeEach(() => {
  calls.length = 0;
  grants.splice(0, grants.length, structuredClone(vipGrant));
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

let router: Router;

// The page opens on the tab (and area) in the address, as a link or the Back button would land on it.
async function open(view?: string, area?: string): Promise<VueWrapper> {
  const queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/app/settings/permissions', component: PermissionsView }],
  });

  await router.push({ path: '/app/settings/permissions', query: { view, area } });
  await router.isReady();

  mounted = mount(PermissionsView, {
    attachTo: document.body,
    global: { plugins: [i18n, router, [VueQueryPlugin, { queryClient: queries }]] },
  });

  await settle();

  return mounted;
}

const texts = (selector: string): string[] =>
  [...document.body.querySelectorAll(selector)].map((el) =>
    (el.textContent ?? '').replace(/\s+/g, ' ').trim());

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

const countOf = (method: string, suffix: string): number =>
  calls.filter((call) => call.method === method && call.path.endsWith(suffix)).length;

function selectLabeled(wrapper: VueWrapper, label: string) {
  const found = wrapper
    .findAllComponents(OptionSelect)
    .find((select) => select.props('label') === label);

  expect(found, `no select labeled ${label}`).toBeDefined();

  return found!;
}

describe('Permissions', () => {
  it('shows each person with the role name, never a number, and locks the broadcaster', async () => {
    await open();

    expect(q('[data-member="modmary"]').textContent).toContain('Moderator');
    expect(q('[data-member="ownerolly"]').textContent).toContain('Broadcaster');
    expect(q('[data-member="ownerolly"]').textContent).not.toMatch(/\d/);
    expect(q('[data-member="ownerolly"] [data-member-remove]').hasAttribute('disabled')).toBe(true);
    expect(q('[data-member="modmary"] [data-member-remove]').hasAttribute('disabled')).toBe(false);
  });

  it('locks only the real owner: a stored Broadcaster label on someone else stays editable', async () => {
    const wrapper = await open();

    expect(q('[data-member="stalesam"] [data-member-remove]').hasAttribute('disabled')).toBe(false);
    expect(selectLabeled(wrapper, 'Role of stalesam').props('disabled')).toBe(false);
    expect(selectLabeled(wrapper, 'Role of ownerolly').props('disabled')).toBe(true);
  });

  it('changes a role with a PUT of the person and the role index, then reloads the team', async () => {
    const wrapper = await open();
    const reads = countOf('GET', '/roles');

    selectLabeled(wrapper, 'Role of modmary').vm.$emit('update:modelValue', 'LeadModerator');
    await settle();

    const sent = callTo('PUT', '/roles');

    expect(sent?.body).toEqual({ userId: 'u-mod', role: 1 });
    expect(countOf('GET', '/roles')).toBe(reads + 1);
  });

  it('removes a person from the team with a DELETE of their id', async () => {
    await open();
    await click(q('[data-member="modmary"] [data-member-remove]'));

    expect(callTo('DELETE', '/roles/{userId}')?.params).toMatchObject({
      path: { userId: 'u-mod' },
    });
  });

  it('sends the ladder value of the picked role and offers nothing below the floor', async () => {
    const wrapper = await open('actions', 'chat');
    const options: readonly Option[] = selectLabeled(wrapper, 'Lowest role for Time a viewer out').props(
      'options',
    );

    expect(options.find((option) => option.value === 'Everyone')?.disabled).toBe(true);
    expect(options.find((option) => option.value === 'Moderator')?.disabled).toBe(false);

    selectLabeled(wrapper, 'Lowest role for Allow one link').vm.$emit(
      'update:modelValue',
      'LeadModerator',
    );

    await settle();

    const sent = callTo('PUT', '/action-permissions/{actionKey}');

    expect(sent?.params).toMatchObject({ path: { actionKey: 'chat.permit-link' } });
    expect(sent?.body).toEqual({ level: 20 });
  });

  it('resets only an action that has an override, with a DELETE of its key', async () => {
    await open('actions', 'chat');

    expect(
      document.body.querySelector('[data-action="chat.permit-link"] [data-action-reset]'),
    ).toBeNull();

    expect(
      document.body.querySelector('[data-action="chat.timeout"] [data-action-reset]'),
    ).not.toBeNull();

    await click(q('[data-action="chat.timeout"] [data-action-reset]'));

    expect(callTo('DELETE', '/action-permissions/{actionKey}')?.params).toMatchObject({
      path: { actionKey: 'chat.timeout' },
    });
  });

  it('gives a capability to the picked person and lists only grantable actions', async () => {
    const wrapper = await open('capabilities');
    const panel = q('[data-panel="person-grants"]');
    const capability: readonly Option[] = selectLabeled(wrapper, 'Capability').props('options');

    expect(capability.map((option) => option.value)).toEqual([
      'chat.permit-link',
      'obs:control',
      'bot:manager:author',
    ]);

    expect(panel.querySelector('[data-grant-add]')?.hasAttribute('disabled')).toBe(true);

    await type(
      panel.querySelector<HTMLInputElement>('[data-person-search]') as HTMLInputElement,
      'newb',
    );

    await click(q('[data-panel="person-grants"] [data-person-option]'));

    selectLabeled(wrapper, 'Capability').vm.$emit('update:modelValue', 'chat.permit-link');
    await settle();
    await click(q('[data-panel="person-grants"] [data-grant-add]'));

    expect(callTo('POST', '/permissions')?.body).toEqual({
      userId: 'u-new',
      permission: 'chat.permit-link',
    });
  });

  it('names the two sensitive grants in plain words in the picker, never the raw key', async () => {
    const wrapper = await open('capabilities');
    const capability: readonly Option[] = selectLabeled(wrapper, 'Capability').props('options');
    const labels = Object.fromEntries(capability.map((option) => [option.value, option.label]));

    expect(labels['obs:control']).toBe('OBS control');
    expect(labels['bot:manager:author']).toBe('Bot manager');
  });

  it('names every other capability in plain words too, in the picker and on the row', async () => {
    const wrapper = await open('capabilities');
    const capability: readonly Option[] = selectLabeled(wrapper, 'Capability').props('options');

    expect(capability.find((option) => option.value === 'chat.permit-link')?.label).toBe('Allow one link');
    expect(q('[data-grant="u-vip:chat.permit-link"]').textContent).toContain('Allow one link');
    expect(q('[data-grant="u-vip:chat.permit-link"]').textContent).not.toContain('chat.permit-link');
  });

  async function pick(wrapper: VueWrapper, permission: string): Promise<void> {
    await type(q<HTMLInputElement>('[data-panel="person-grants"] [data-person-search]'), 'newb');
    await click(q('[data-panel="person-grants"] [data-person-option]'));
    selectLabeled(wrapper, 'Capability').vm.$emit('update:modelValue', permission);
    await settle();
    await click(q('[data-panel="person-grants"] [data-grant-add]'));
  }

  it('shows what OBS control lets the person do and sends nothing until confirmed', async () => {
    const wrapper = await open('capabilities');

    await pick(wrapper, 'obs:control');

    expect(callTo('POST', '/permissions')).toBeUndefined();
    expect(document.body.textContent).toContain('switch scenes');

    await click(q('[data-confirm-action]'));

    expect(callTo('POST', '/permissions')?.body).toEqual({
      userId: 'u-new',
      permission: 'obs:control',
    });

    expect(q('[data-grant="u-new:obs:control"]').textContent).toContain('newbie');
  });

  it('sends nothing when the OBS control confirm is canceled', async () => {
    const wrapper = await open('capabilities');

    await pick(wrapper, 'obs:control');
    await click(q('[role="alertdialog"] button:not([data-confirm-action])'));

    expect(callTo('POST', '/permissions')).toBeUndefined();
  });

  it('makes a person a Bot manager after a confirm that says what the role cannot do', async () => {
    const wrapper = await open('capabilities');

    await pick(wrapper, 'bot:manager:author');

    expect(callTo('POST', '/permissions')).toBeUndefined();
    expect(document.body.textContent).toContain('cannot moderate');

    await click(q('[data-confirm-action]'));

    expect(callTo('POST', '/permissions')?.body).toEqual({
      userId: 'u-new',
      permission: 'bot:manager:author',
    });

    expect(q('[data-grant="u-new:bot:manager:author"]').textContent).toContain('Bot manager');
  });

  it('revokes OBS control and the Bot manager role with the key in the query and drops the row', async () => {
    grants.push(
      {
        id: 8,
        subjectType: 'user',
        subjectId: 'u-obs',
        subjectName: 'obsollie',
        resourceType: 'action',
        resourceId: 'obs:control',
        permissionValue: 'allow',
      },
      {
        id: 9,
        subjectType: 'user',
        subjectId: 'u-bm',
        subjectName: 'bammy',
        resourceType: 'action',
        resourceId: 'bot:manager:author',
        permissionValue: 'allow',
      },
    );

    await open('capabilities');
    await click(q('[data-grant="u-obs:obs:control"] [data-grant-revoke]'));

    expect(callTo('DELETE', '/permissions/{userId}')?.params).toMatchObject({
      path: { userId: 'u-obs' },
      query: { permission: 'obs:control' },
    });

    expect(document.body.querySelector('[data-grant="u-obs:obs:control"]')).toBeNull();
    expect(q('[data-grant="u-bm:bot:manager:author"]').textContent).toContain('Bot manager');

    await click(q('[data-grant="u-bm:bot:manager:author"] [data-grant-revoke]'));

    expect(document.body.querySelector('[data-grant="u-bm:bot:manager:author"]')).toBeNull();
  });

  it('takes a capability away with the permission in the query', async () => {
    await open('capabilities');
    await click(q('[data-grant="u-vip:chat.permit-link"] [data-grant-revoke]'));

    const sent = callTo('DELETE', '/permissions/{userId}');

    expect(sent?.params).toMatchObject({
      path: { userId: 'u-vip' },
      query: { permission: 'chat.permit-link' },
    });
  });
});

describe('Permissions layout', () => {
  it('shows one tab at a time, the team first, with counts that match the lists', async () => {
    await open();

    expect(texts('[role="tab"]')).toEqual([
      'Team 3',
      'Who may do what 5',
      'Personal capabilities 1',
    ]);

    expect(document.body.querySelector('[data-member="modmary"]')).not.toBeNull();
    expect(document.body.querySelector('[data-panel="action-matrix"]')).toBeNull();
    expect(document.body.querySelector('[data-panel="person-grants"]')).toBeNull();
  });

  it('opens a tab with its name in the address, so Back returns to the last one', async () => {
    await open();

    const tab = [...document.body.querySelectorAll('[role="tab"]')][2];

    tab?.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, button: 0 }));
    await settle();

    expect(router.currentRoute.value.query).toMatchObject({ view: 'capabilities' });

    expect(document.body.querySelector('[data-panel="person-grants"]')).not.toBeNull();
    expect(document.body.querySelector('[data-member="modmary"]')).toBeNull();
  });

  it('lists the actions by area in plain words, with how many each holds and how many changed', async () => {
    await open('actions');

    expect(texts('[data-area] [data-slot="summary-row-title"]')).toEqual(['Bot manager', 'Chat', 'OBS']);
    expect(q('[data-area="chat"]').textContent).toContain('3 actions');
    expect(q('[data-area="chat"]').textContent).toContain('1 changed');
    expect(q('[data-area="obs"]').textContent).not.toContain('changed');
  });

  it('shows only the actions of the picked area and keeps the area in the address', async () => {
    await open('actions');

    expect(document.body.querySelector('[data-action="bot:manager:author"]')).not.toBeNull();
    expect(document.body.querySelector('[data-action="chat.timeout"]')).toBeNull();

    await click(q('[data-area="obs"]'));

    expect(router.currentRoute.value.query).toMatchObject({ view: 'actions', area: 'obs' });
    expect(texts('[data-action] [data-slot="summary-row-title"]')).toEqual(['Switch scenes and sources in OBS']);
  });

  it('names each action in plain words with the bot description below, never the key or the floor again', async () => {
    await open('actions', 'obs');

    const row = q('[data-action="obs:control"]');

    expect(row.querySelector('[data-slot="summary-row-title"]')?.textContent?.trim()).toBe(
      'Switch scenes and sources in OBS',
    );

    expect(row.textContent).toContain('Control OBS');
    expect(row.textContent).not.toContain('obs:control');
    expect(row.textContent).not.toContain('Lowest allowed');
  });

  it('names an action the dashboard does not know by the bot description, else by its area', async () => {
    await open('actions', 'chat');

    expect(texts('[data-action] [data-slot="summary-row-title"]')).toEqual([
      'Allow one link',
      'An action in Chat',
      'Time a viewer out',
    ]);
  });

  it('gives every action the server seeds an English and a Dutch name', () => {
    const root = process.cwd();
    const seeder = readFileSync(
      resolve(root, '../server/src/NomNomzBot.Infrastructure/Content/Identity/ActionDefinitionSeeder.cs'),
      'utf8',
    );
    const keys = [...seeder.matchAll(/^\s+[A-Za-z]+\("([a-z][a-z0-9_:.-]*)"/gm)].map((match) => match[1] ?? '');
    const named = (names: Record<string, string>): string[] =>
      keys.filter((key) => names[actionNameSlug(key)] === undefined);

    expect(keys.length).toBeGreaterThan(200);
    expect(named(en.dashboard.settings.permissions.action)).toEqual([]);
    expect(named(nl.dashboard.settings.permissions.action)).toEqual([]);
  });

  it('finds an action by its key or description across every area', async () => {
    await open('actions', 'chat');
    await type(q<HTMLInputElement>('[data-area-search] input'), 'one link');

    expect(texts('[data-area] [data-slot="summary-row-title"]')).toEqual(['Chat']);
    expect(q('[data-area="chat"]').textContent).toContain('1 action');
    expect(texts('[data-action] [data-slot="summary-row-title"]')).toEqual(['Allow one link']);
  });

  it('finds an action by its plain name', async () => {
    await open('actions');
    await type(q<HTMLInputElement>('[data-area-search] input'), 'scenes');

    expect(texts('[data-area] [data-slot="summary-row-title"]')).toEqual(['OBS']);
  });
});

describe('Permissions route', () => {
  it('opens the real page from the sidebar entry, not a coming-soon scaffold', async () => {
    const entry = settingsNav
      .flatMap((group) => group.entries)
      .find((item) => item.route === 'dashboard-settings-permissions');
    const record = dashboardSettingsRoutes.find(
      (route) => route.name === 'dashboard-settings-permissions',
    );
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ ...record!, path: `/app/${record!.path}` }],
    });
    const loaded = await (record!.component as () => Promise<{ default: unknown }>)();

    expect(entry).toBeDefined();

    expect(router.resolve({ name: 'dashboard-settings-permissions' }).path).toBe(
      '/app/settings/permissions',
    );

    expect(record?.meta).toEqual({ navMode: 'settings', action: 'roles:read' });
    expect(loaded.default).toBe(PermissionsView);
  });
});
