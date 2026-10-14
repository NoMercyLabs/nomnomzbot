// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
