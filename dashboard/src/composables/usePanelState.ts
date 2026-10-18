// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
