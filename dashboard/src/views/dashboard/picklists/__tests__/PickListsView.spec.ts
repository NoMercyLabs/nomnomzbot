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
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ref } from 'vue';

import { i18n } from '@/i18n';
import common from '@/i18n/locales/en.json';

import PickListsView from '../PickListsView.vue';
import en from '../i18n/en.json';

type PickListRow = {
  id: string;
  name: string;
  description: string | null;
  items: string[];
  createdAt: string;
  updatedAt: string;
};

const list = (id: string, name: string, items: string[]): PickListRow => ({
  id,
  name,
  description: null,
  items,
  createdAt: '2026-10-01T12:00:00Z',
  updatedAt: '2026-10-01T12:00:00Z',
});

let lists: PickListRow[] = [];
let blastCount = 0;
const calls: string[] = [];
const bodies: Record<string, unknown>[] = [];

const ok = { status: 200, ok: true };

type PutInit = { body: Record<string, unknown>; params: { path: { id: string } } };

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string) => {
      calls.push(`GET ${path}`);

      if (path.endsWith('/blast-radius')) {
        return {
          data: {
            data: {
              isMinimum: false,
              categories: blastCount === 0
                ? []
                : [
                    {
                      categoryKey: 'blast_radius_category_pipeline_steps',
                      count: blastCount,
                      sample: [],
                    },
                  ],
            },
          },
          response: ok,
        };
      }

      return { data: {
        data: lists,
        total: lists.length,
        hasMore: false,
      }, response: ok };
    },
    POST: async (path: string, init: { body: Record<string, unknown> }) => {
      calls.push(`POST ${path}`);
      bodies.push(init.body);
      lists = [...lists, list('new', String(init.body.name), init.body.items as string[])];

      return { data: {}, response: ok };
    },
    PUT: async (path: string, init: PutInit) => {
      calls.push(`PUT ${path} ${init.params.path.id}`);
      bodies.push(init.body);

      lists = lists.map((row) =>
        row.id === init.params.path.id
          ? {
              ...row,
              name: String(init.body.name),
              items: init.body.items as string[],
            }
          : row);

      return { data: {}, response: ok };
    },
    DELETE: async (path: string, init: { params: { path: { id: string } } }) => {
      calls.push(`DELETE ${path} ${init.params.path.id}`);
      lists = lists.filter((row) => row.id !== init.params.path.id);

      return { data: {}, response: ok };
    },
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

const held = vi.hoisted(() => ({ keys: null as Set<string> | null }));

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({
    known: ref(true),
    can: (key: string) => held.keys === null || held.keys.has(key),
  }),
}));

let mounted: VueWrapper | null = null;

beforeEach(() => {
  localStorage.clear();
  held.keys = null;
  calls.length = 0;
  bodies.length = 0;
  blastCount = 0;
  setActivePinia(createPinia());
  i18n.global.mergeLocaleMessage('en', common);
  i18n.global.mergeLocaleMessage('en', en);

  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe(): void {}
      unobserve(): void {}
      disconnect(): void {}
    },
  );
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

async function settle(): Promise<void> {
  await flushPromises();
  await new Promise((done) => setTimeout(done, 400));
  await flushPromises();
}

async function open(): Promise<void> {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  mounted = mount(PickListsView, {
    attachTo: document.body,
    global: { plugins: [i18n, [VueQueryPlugin, { queryClient }]] },
  });

  await settle();
}

async function fill(selector: string, value: string): Promise<void> {
  const input = document.querySelector<HTMLInputElement>(selector);

  if (!input)
    throw new Error(`missing ${selector}`);

  input.value = value;
  input.dispatchEvent(new Event('input', { bubbles: true }));
  await flushPromises();
}

function submitDialog(): void {
  document
    .querySelector('form')
    ?.dispatchEvent(new Event('submit', { cancelable: true, bubbles: true }));
}

const rows = (): string[] =>
  Array.from(document.querySelectorAll('[data-picklist-row]')).map((row) => row.textContent ?? '');

describe('PickListsView', () => {
  it('lists each pick list with its entry count and the helper that draws from it', async () => {
    lists = [list('a', 'jokes', ['one', 'two']), list('b', 'greetings', ['hi'])];
    await open();

    expect(rows()).toHaveLength(2);
    expect(rows()[0]).toContain('jokes');
    expect(rows()[0]).toContain('2 entries');
    expect(rows()[0]).toContain('{list.pick.jokes}');
    expect(rows()[1]).toContain('1 entry');
  });

  it('adds a pick list with trimmed name and one entry per non-empty line', async () => {
    lists = [];
    await open();

    document.querySelector<HTMLElement>('[data-picklist-new]')?.click();
    await settle();
    await fill('[data-picklist-name]', ' puns ');
    await fill('[data-picklist-items]', 'first\n\n  second  \n');
    submitDialog();
    await settle();

    expect(calls).toContain('POST /api/v{version}/picklists');

    expect(bodies[0]).toEqual({
      name: 'puns',
      description: null,
      items: ['first', 'second'],
    });

    expect(rows()).toHaveLength(1);
    expect(rows()[0]).toContain('puns');
    expect(rows()[0]).toContain('2 entries');
  });

  it('renames and edits a pick list by its id, replacing the entries', async () => {
    lists = [list('a', 'jokes', ['one'])];
    await open();

    document.querySelector<HTMLElement>('[data-picklist-edit]')?.click();
    await settle();

    expect(document.querySelector<HTMLInputElement>('[data-picklist-name]')?.value).toBe('jokes');

    await fill('[data-picklist-name]', 'dad jokes');
    await fill('[data-picklist-items]', 'one\ntwo\nthree');
    submitDialog();
    await settle();

    expect(calls).toContain('PUT /api/v{version}/picklists/{id} a');

    expect(bodies[0]).toEqual({
      name: 'dad jokes',
      description: null,
      items: ['one', 'two', 'three'],
    });

    expect(rows()[0]).toContain('dad jokes');
    expect(rows()[0]).toContain('3 entries');
  });

  it('refuses a list without a name and says why, without saving', async () => {
    lists = [];
    await open();

    document.querySelector<HTMLElement>('[data-picklist-new]')?.click();
    await settle();
    submitDialog();
    await settle();

    expect(calls.filter((call) => call.startsWith('POST'))).toEqual([]);
    expect(document.body.textContent).toContain(en.dashboard.picklists.dialog.problem.nameRequired);
  });

  it('shows the counted pipeline steps before the delete and removes the list after the confirm', async () => {
    lists = [list('a', 'jokes', ['one'])];
    blastCount = 3;
    await open();

    document.querySelector<HTMLElement>('[data-picklist-delete]')?.click();
    await settle();

    expect(calls).toContain('GET /api/v{version}/picklists/{id}/blast-radius');
    expect(calls.filter((call) => call.startsWith('DELETE'))).toEqual([]);
    expect(document.querySelector('[data-slot="blast-list"]')?.textContent).toContain('3 pipeline steps');

    document.querySelector<HTMLElement>('[data-confirm-action]')?.click();
    await settle();

    expect(calls).toContain('DELETE /api/v{version}/picklists/{id} a');
    expect(rows()).toHaveLength(0);
  });

  it('says plainly that nothing uses the list when the count is zero', async () => {
    lists = [list('a', 'jokes', ['one'])];
    await open();

    document.querySelector<HTMLElement>('[data-picklist-delete]')?.click();
    await settle();

    expect(document.querySelector('[data-slot="blast-list"]')?.textContent).toContain(
      en.dashboard.picklists.delete.nothing,
    );
  });

  it('hides add, edit and delete from a person who may only read', async () => {
    held.keys = new Set(['picklists:read']);
    lists = [list('a', 'jokes', ['one'])];
    await open();

    expect(rows()).toHaveLength(1);
    expect(document.querySelector('[data-picklist-new]')).toBeNull();
    expect(document.querySelector('[data-picklist-edit]')).toBeNull();
    expect(document.querySelector('[data-picklist-delete]')).toBeNull();
  });
});
