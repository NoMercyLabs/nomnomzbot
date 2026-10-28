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

import { computed, toValue, type MaybeRefOrGetter, type WritableComputedRef } from 'vue';
import { useRoute, useRouter } from 'vue-router';

// The open tab of a page lives in the route query (?view=), so a link opens a tab and Back returns to the last one.
// A missing or unknown value falls back to the first tab; the query stays clean until a person picks another tab.
// A second tab row on the same page (the phone board) keeps its own query key.
// A fixed list of tabs (a non-empty tuple, usually keysOf(TABLE)) gives back the union of its names. A page that
// hides tabs from some people passes the tabs they may open as `shown`; the first of those is the fallback.
// A list built at run time by a component (tab strip, board panels) is typed by its own generic and can be empty, so
// its view is `View | undefined`; no signature hands back a bare string.
export function usePageView<const View extends string>(
  views: readonly [View, ...View[]],
  key?: string,
  shown?: MaybeRefOrGetter<readonly View[]>,
): WritableComputedRef<View>;

export function usePageView<View extends string>(
  views: MaybeRefOrGetter<readonly View[]>,
  key?: string,
): WritableComputedRef<View | undefined>;

export function usePageView<View extends string>(
  views: MaybeRefOrGetter<readonly View[]>,
  key = 'view',
  shown?: MaybeRefOrGetter<readonly View[]>,
): WritableComputedRef<View | undefined> {
  const route = useRoute();
  const router = useRouter();

  return computed<View | undefined>({
    get: () => {
      const known = toValue(shown ?? views);

      return known.find((view) => view === route.query[key]) ?? known[0] ?? toValue(views)[0];
    },
    set: (view): void => {
      if (view === undefined) return;

      // A board panel id (?panel=) belongs to the tab it was opened on, so another tab drops it.
      const { panel: _stale, ...kept } = route.query;
      const query = key === 'panel'
        ? route.query
        : kept;

      void router.push({ query: { ...query, [key]: view } });
    },
  });
}
