// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { computed, type Ref, type WritableComputedRef } from 'vue';

// A dialog about one thing: open while a target is held, and closing it lets the target go.
export function useTargetOpen<T>(target: Ref<T | null>): WritableComputedRef<boolean> {
  return computed<boolean>({
    get: () => target.value !== null,
    set: (open: boolean) => {
      if (!open) {
        target.value = null;
      }
    },
  });
}
