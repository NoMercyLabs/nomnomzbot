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

import { ref, watch, type Ref } from 'vue';

export type NumberFieldOptions = {
  // The value the server holds; the box follows it whenever it changes.
  stored: () => number | undefined;
  fewest: number;
  // The most the server accepts, once it is known.
  most: () => number | undefined;
  // Called only with a whole number inside the range that differs from the stored value.
  commit: (next: number) => void;
};

export type NumberField = {
  text: Ref<string>;
  save: () => void;
};

// A number box that saves when it loses focus or Enter is pressed: the text is made a whole number, held inside
// the range, shown back in the box, and sent only when it differs from what the server holds.
export function useNumberField(options: NumberFieldOptions) {
  const text = ref('');

  watch(
    options.stored,
    (current) => {
      text.value = Number.isFinite(current)
        ? String(current)
        : '';
    },
    { immediate: true },
  );

  function save(): void {
    const parsed = Math.trunc(Number(text.value));
    const next = Number.isFinite(parsed)
      ? Math.min(options.most() ?? parsed, Math.max(options.fewest, parsed))
      : options.fewest;

    text.value = String(next);

    if (next !== options.stored()) {
      options.commit(next);
    }
  }

  return { text, save } as const satisfies NumberField;
}
