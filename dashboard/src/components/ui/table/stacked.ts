// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { ComputedRef, InjectionKey } from 'vue'
import { computed, inject } from 'vue'

// A Table with stacked set renders every row as a card below md; its parts read the flag from here.
export const tableStackedKey: InjectionKey<ComputedRef<boolean>> = Symbol('table-stacked')

export function useTableStacked() {
  return inject(tableStackedKey, computed(() => false))
}
