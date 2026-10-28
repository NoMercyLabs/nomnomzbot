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

import type { ComputedRef, InjectionKey } from 'vue'
import { computed, inject } from 'vue'

// A Table with stacked set renders every row as a card below md; its parts read the flag from here.
export const tableStackedKey: InjectionKey<ComputedRef<boolean>> = Symbol('table-stacked')

export function useTableStacked() {
  return inject(tableStackedKey, computed(() => false))
}
