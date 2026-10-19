// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
