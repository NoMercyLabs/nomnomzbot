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
import OptionSelect, { type Option } from '../components/OptionSelect.vue';
import en from '../i18n/en.json';
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
];

const grants = [
  {
    id: 1,
    subjectType: 'user',
    subjectId: 'u-vip',
    subjectName: 'vipvic',
    resourceType: 'action',
    resourceId: 'chat.permit-link',
    permissionValue: 'allow',
  },
];

const found = [{
  id: 'u-new',
  label: 'newbie',
  subLabel: 'seen in chat',
  inChannel: true,
}];

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
        return page(grants);

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

async function open(): Promise<VueWrapper> {
  const queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  mounted = mount(PermissionsView, {
    attachTo: document.body,
    global: { plugins: [i18n, [VueQueryPlugin, { queryClient: queries }]] },
  });

  await settle();

  return mounted;
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

const countOf = (method: string, suffix: string): number =>
  calls.filter((call) => call.method === method && call.path.endsWith(suffix)).length;

function selectLabeled(wrapper: VueWrapper, label: string) {
  const found = wrapper.findAllComponents(OptionSelect).find((select) => select.props('label') === label);

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

    expect(callTo('DELETE', '/roles/{userId}')?.params).toMatchObject({ path: { userId: 'u-mod' } });
  });

  it('sends the ladder value of the picked role and offers nothing below the floor', async () => {
    const wrapper = await open();
    const options: readonly Option[] = selectLabeled(wrapper, 'Lowest role for chat.timeout').props('options');

    expect(options.find((option) => option.value === 'Everyone')?.disabled).toBe(true);
    expect(options.find((option) => option.value === 'Moderator')?.disabled).toBe(false);

    selectLabeled(wrapper, 'Lowest role for chat.permit-link').vm.$emit('update:modelValue', 'LeadModerator');
    await settle();

    const sent = callTo('PUT', '/action-permissions/{actionKey}');

    expect(sent?.params).toMatchObject({ path: { actionKey: 'chat.permit-link' } });
    expect(sent?.body).toEqual({ level: 20 });
  });

  it('resets only an action that has an override, with a DELETE of its key', async () => {
    await open();

    expect(document.body.querySelector('[data-action="chat.permit-link"] [data-action-reset]')).toBeNull();
    expect(document.body.querySelector('[data-action="chat.timeout"] [data-action-reset]')).not.toBeNull();

    await click(q('[data-action="chat.timeout"] [data-action-reset]'));

    expect(callTo('DELETE', '/action-permissions/{actionKey}')?.params).toMatchObject({
      path: { actionKey: 'chat.timeout' },
    });
  });

  it('gives a capability to the picked person and lists only grantable actions', async () => {
    const wrapper = await open();
    const panel = q('[data-panel="person-grants"]');
    const capability: readonly Option[] = selectLabeled(wrapper, 'Capability').props('options');

    expect(capability.map((option) => option.value)).toEqual(['chat.permit-link']);
    expect(panel.querySelector('[data-grant-add]')?.hasAttribute('disabled')).toBe(true);

    await type(panel.querySelector<HTMLInputElement>('[data-person-search]') as HTMLInputElement, 'newb');
    await click(q('[data-panel="person-grants"] [data-person-option]'));

    selectLabeled(wrapper, 'Capability').vm.$emit('update:modelValue', 'chat.permit-link');
    await settle();
    await click(q('[data-panel="person-grants"] [data-grant-add]'));

    expect(callTo('POST', '/permissions')?.body).toEqual({ userId: 'u-new', permission: 'chat.permit-link' });
  });

  it('takes a capability away with the permission in the query', async () => {
    await open();
    await click(q('[data-grant="u-vip:chat.permit-link"] [data-grant-revoke]'));

    const sent = callTo('DELETE', '/permissions/{userId}');

    expect(sent?.params).toMatchObject({
      path: { userId: 'u-vip' },
      query: { permission: 'chat.permit-link' },
    });
  });
});

describe('Permissions route', () => {
  it('opens the real page from the sidebar entry, not a coming-soon scaffold', async () => {
    const entry = settingsNav
      .flatMap((group) => group.entries)
      .find((item) => item.route === 'dashboard-settings-permissions');
    const record = dashboardSettingsRoutes.find((route) => route.name === 'dashboard-settings-permissions');
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ ...record!, path: `/app/${record!.path}` }],
    });
    const loaded = await (record!.component as () => Promise<{ default: unknown }>)();

    expect(entry).toBeDefined();
    expect(router.resolve({ name: 'dashboard-settings-permissions' }).path).toBe('/app/settings/permissions');
    expect(record?.meta).toEqual({ navMode: 'settings', action: 'roles:read' });
    expect(loaded.default).toBe(PermissionsView);
  });
});
