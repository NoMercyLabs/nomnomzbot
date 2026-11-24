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

import { readonly, ref } from 'vue';

import type { components } from '@/api/schema';

export type MissingSource = components['schemas']['MissingSource'];

// The browser sources a just-saved pipeline needs that are not open in OBS. A save with nothing missing
// announces an empty list, so the one MissingSourcesDialog opens only when something is left to say.
const items = ref<MissingSource[]>([]);

function announce(missing: readonly MissingSource[]): void {
  items.value = missing.slice();
}

function dismiss(): void {
  items.value = [];
}

export function useMissingSources() {
  return {
    items: readonly(items),
    announce,
    dismiss,
  };
}
