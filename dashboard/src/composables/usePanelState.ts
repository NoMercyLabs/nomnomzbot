// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { computed, type ComputedRef, type Ref } from 'vue'

import type { PanelState } from '@/components/layout/PanelCard.vue'

type PanelQuery<T> = { data: Ref<T | undefined>; isPending: Ref<boolean>; isError: Ref<boolean> }

// One query feeds one panel: an error wins, then loading, then the empty check on the loaded data.
export function usePanelState<T>(
  query: PanelQuery<T>,
  isEmpty: (data: T) => boolean = () => false,
): ComputedRef<PanelState> {
  return computed<PanelState>(() => {
    if (query.isError.value)
      return 'error'

    if (query.isPending.value || query.data.value === undefined)
      return 'loading'

    return isEmpty(query.data.value)
      ? 'empty'
      : 'ready'
  })
}
