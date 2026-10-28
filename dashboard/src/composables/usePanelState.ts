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

import { computed, type Ref } from 'vue';

type PanelQuery<T> = {
  data: Ref<T | undefined>;
  isPending: Ref<boolean>;
  isError: Ref<boolean>;
};

// The state a panel shows; it comes from the literals the function below returns.
export type PanelState = ReturnType<typeof usePanelState>['value'];

// One query feeds one panel: an error wins, then loading, then the empty check on the loaded data.
export function usePanelState<const T>(
  query: PanelQuery<T>,
  isEmpty: (data: T) => boolean = () => false,
) {
  return computed(() => {
    if (query.isError.value)
      return 'error' as const;

    if (query.isPending.value || query.data.value === undefined)
      return 'loading' as const;

    return isEmpty(query.data.value)
      ? ('empty' as const)
      : ('ready' as const);
  });
}
