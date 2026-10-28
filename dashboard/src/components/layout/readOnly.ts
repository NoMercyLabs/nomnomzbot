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

import { computed, inject, type InjectionKey, type Ref } from 'vue';

// Provided by ReadOnlyScope: true while a scope above this component is locked.
export const readOnlyKey = Symbol('readOnly') as InjectionKey<Ref<boolean>>;

// The inherited lock. Without a scope above, nothing is locked. A dialog renders in a portal, so a
// fieldset cannot reach it; it reads this instead and switches its own actions off.
export function useReadOnly(): Ref<boolean> {
  return inject(
    readOnlyKey,
    computed(() => false),
  );
}
