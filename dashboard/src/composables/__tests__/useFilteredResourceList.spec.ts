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

import { VueQueryPlugin } from '@tanstack/vue-query';
import { mount } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';
import { defineComponent, h, ref } from 'vue';
import { createMemoryHistory, createRouter, type Router } from 'vue-router';

import { queryClient } from '@/api/queryClient';

import { useFilteredResourceList } from '../useFilteredResourceList';

type Item = {
  id: string;
  name: string;
  note: string;
  on: boolean;
};

const ITEMS: Item[] = [
  {
    id: 'a',
    name: 'Alpha',
    note: 'first',
    on: true,
  },
  {
    id: 'b',
    name: 'Beta',
    note: 'second letter',
    on: false,
  },
  {
    id: 'c',
    name: 'Gamma',
    note: '',
    on: true,
  },
];

const FILTERS = {
  all: () => true,
  on: (item: Item) => item.on,
  off: (item: Item) => !item.on,
  none: () => false,
} as const satisfies Record<string, (item: Item) => boolean>;

type Opened = {
  router: Router;
  list: ReturnType<typeof useFilteredResourceList<Item, keyof typeof FILTERS>>;
  data: ReturnType<typeof ref<Item[] | undefined>>;
};

async function open(query: string, loaded: Item[] | 'pending' = ITEMS): Promise<Opened> {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/app/x', component: { template: '<div />' } }],
  });

  await router.push(`/app/x${query}`);

  const data = ref<Item[] | undefined>(loaded === 'pending'
    ? undefined
    : loaded);
  let list!: Opened['list'];

  mount(
    defineComponent({
      setup: () => {
        list = useFilteredResourceList({
          query: {
            data,
            isPending: ref(false),
            isError: ref(false),
          },
          items: () => data.value,
          filters: FILTERS,
          tabLabel: (tab) => `Tab ${tab}`,
          searchText: (item) => [item.name, item.note],
          paneKey: 'item',
        });

        return () => h('div');
      },
    }),
    { global: { plugins: [router, [VueQueryPlugin, { queryClient }]] } },
  );

  return {
    router,
    list,
    data,
  };
}

describe('useFilteredResourceList', () => {
  it('counts every filter from the loaded items and labels the tabs from the page words', async () => {
    const { list } = await open('');

    expect(list.tabs.value).toEqual([
      {
        value: 'all',
        label: 'Tab all',
        count: 3,
      },
      {
        value: 'on',
        label: 'Tab on',
        count: 2,
      },
      {
        value: 'off',
        label: 'Tab off',
        count: 1,
      },
      {
        value: 'none',
        label: 'Tab none',
        count: 0,
      },
    ]);
  });

  it('shows only the items of the open tab, and the count follows the tab', async () => {
    const { list } = await open('?view=on');

    expect(list.visible.value?.map((item) => item.id)).toEqual(['a', 'c']);
    expect(list.count.value).toBe(2);
  });

  it('narrows by every text searchText names, ignoring case and outer spaces', async () => {
    const { list } = await open('');

    list.search.value = '  SECOND ';

    expect(list.visible.value?.map((item) => item.id)).toEqual(['b']);
    expect(list.count.value).toBe(1);
  });

  it('reports no items as none, a search with no hit as noMatch with the typed query, and an empty tab as filtered', async () => {
    const none = await open('', []);

    expect(none.list.panelState.value).toBe('empty');

    expect(none.list.empty.value).toEqual({
      kind: 'none',
      query: '',
    });

    const searched = await open('');

    searched.list.search.value = ' zzz ';

    expect(searched.list.panelState.value).toBe('empty');

    expect(searched.list.empty.value).toEqual({
      kind: 'noMatch',
      query: 'zzz',
    });

    const tab = await open('?view=none');

    expect(tab.list.panelState.value).toBe('empty');
    expect(tab.list.empty.value.kind).toBe('filtered');
  });

  it('is loading until the items arrive and ready after', async () => {
    const { list, data } = await open('', 'pending');

    expect(list.panelState.value).toBe('loading');
    const counts = list.tabs.value.map((tab) => tab.count);

    expect(counts).toEqual([undefined, undefined, undefined, undefined]);

    data.value = ITEMS;

    expect(list.panelState.value).toBe('ready');
  });

  it('keeps an item open that the open tab hides, because the link looks in every item', async () => {
    const { list } = await open('?view=on&item=b');

    expect(list.visible.value?.map((item) => item.id)).not.toContain('b');
    expect(list.asked.value).toBe('b');
    expect(list.selectedId.value).toBe('b');
    expect(list.selected.value?.name).toBe('Beta');
  });

  it('has no open item without an id, and no selected item for an unknown id', async () => {
    const empty = await open('');

    expect(empty.list.asked.value).toBe('');
    expect(empty.list.selected.value).toBeNull();

    const unknown = await open('?item=zzz');

    expect(unknown.list.asked.value).toBe('zzz');
    expect(unknown.list.selected.value).toBeNull();
    expect(unknown.list.selectedId.value).toBe('');
  });

  it('opens an item through the pane and writes it into the link', async () => {
    const { list, router } = await open('');

    list.pane.open('c');
    await new Promise((done) => setTimeout(done, 0));

    expect(router.currentRoute.value.query).toEqual({ item: 'c' });
    expect(list.selected.value?.name).toBe('Gamma');
  });
});
