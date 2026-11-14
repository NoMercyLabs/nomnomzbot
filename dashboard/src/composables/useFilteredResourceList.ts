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

import { computed, ref, type Ref } from 'vue';

import type { PageTab } from '@/components/layout/pageTab';
import { usePageView } from '@/composables/usePageView';
import { usePanelState } from '@/composables/usePanelState';
import { useQueryPane } from '@/composables/useQueryPane';
import { keysOf } from '@/lib/array';

// Decision (2026-11-14, design-system TODO "a small filtered-list shell"): this composable stays and no shell
// component joins it. Two pages use it (Overlays and Scripts), and each already passes its own tab and empty
// words into the composable, so the behavior is shared and the words are not. A shell would only repeat the
// markup of two pages. A third page with the same markup is the signal to extract one.

// Why a list shows nothing, for the EmptyState of its panel: none = nothing made yet, noMatch = the search found
// nothing, filtered = the open tab hides every item. The page maps the kind to its own words.
export type ListEmptyKind = 'none' | 'noMatch' | 'filtered';

type ListQuery<T> = {
  data: Ref<T[] | undefined>;
  isPending: Ref<boolean>;
  isError: Ref<boolean>;
};

type FilteredResourceListOptions<T, K extends string> = {
  // The query that feeds the panel: it gives the loading and error state.
  query: ListQuery<T>;
  // The items to filter; undefined until they are loaded.
  items: () => T[] | undefined;
  // One test per tab, in tab order.
  filters: Record<K, (item: T) => boolean>;
  // The words of a tab; the composable holds no text of its own.
  tabLabel: (tab: K) => string;
  // The text of an item the search looks in.
  searchText: (item: T) => string[];
  // The route query key of the open item (?script=).
  paneKey: string;
};

// The filter tabs, their counts, the search, the panel state and the open item of a list page.
// The open item is the id in the route query; it is looked up in every item, not only the visible ones,
// so an item the open tab hides stays open. Counts come from the loaded list: the bot has no count endpoint.
export function useFilteredResourceList<T extends { id: string }, K extends string>(
  options: FilteredResourceListOptions<T, K>,
) {
  const tabValues = keysOf(options.filters);
  const view = usePageView(tabValues);
  const search = ref('');

  function countOf(tab: K): number | undefined {
    return options.items()?.filter(options.filters[tab]).length;
  }

  const tabs = computed<PageTab<K>[]>(() =>
    tabValues.map((value) => ({
      value,
      label: options.tabLabel(value),
      count: countOf(value),
    })));

  const typed = computed(() => search.value.trim());

  function matches(item: T): boolean {
    const needle = typed.value.toLowerCase();
    const texts = options.searchText(item);

    return needle === '' || texts.some((text) => text.toLowerCase().includes(needle));
  }

  const visible = computed<T[] | undefined>(() =>
    options
      .items()
      ?.filter(options.filters[view.value])
      .filter(matches));

  const listState = usePanelState(options.query);
  const panelState = computed(() => {
    if (listState.value !== 'ready')
      return listState.value;

    return visible.value?.length === 0
      ? 'empty'
      : 'ready';
  });

  const kind = computed<ListEmptyKind>(() => {
    if (typed.value !== '')
      return 'noMatch';

    return options.items()?.length === 0
      ? 'none'
      : 'filtered';
  });
  const empty = computed(() => ({
    kind: kind.value,
    query: typed.value,
  }));

  const count = computed<number | undefined>(() =>
    panelState.value === 'ready'
      ? visible.value?.length ?? 0
      : undefined);

  const pane = useQueryPane(options.paneKey);
  const asked = computed<string>(() => pane.id.value ?? '');
  const selected = computed<T | null>(() => {
    if (asked.value === '')
      return null;

    return options.items()?.find((item) => item.id === asked.value) ?? null;
  });
  const selectedId = computed<string>(() => selected.value?.id ?? '');

  return {
    view,
    tabs,
    search,
    visible,
    panelState,
    empty,
    count,
    pane,
    asked,
    selectedId,
    selected,
  } as const;
}
