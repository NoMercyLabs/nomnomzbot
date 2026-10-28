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

import { computed, ref, toValue, type MaybeRefOrGetter } from 'vue';

// A long list shows its first rows and grows by one step on demand, so a panel never becomes the whole page.
export function useShowMore<T>(items: MaybeRefOrGetter<readonly T[]>, step: number = 10) {
  const shown = ref<number>(step);

  const visible = computed<T[]>(() => toValue(items).slice(0, shown.value));
  const remaining = computed<number>(() => Math.max(0, toValue(items).length - shown.value));
  const hasMore = computed<boolean>(() => remaining.value > 0);

  function more(): void {
    shown.value += step;
  }

  return {
    visible,
    remaining,
    hasMore,
    more,
  };
}
