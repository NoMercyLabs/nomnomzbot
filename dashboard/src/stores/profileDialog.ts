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

import { defineStore } from 'pinia';
import { ref } from 'vue';

// Which person the one profile dialog shows. A name anywhere in the dashboard calls open();
// a second call swaps the person, it never makes a second dialog. The opener gets the focus back on close.
export const useProfileDialogStore = defineStore('profileDialog', () => {
  const userId = ref<string | null>(null);
  const opener = ref<HTMLElement | null>(null);

  function open(id: string, from: HTMLElement | null = null): void {
    userId.value = id;

    if (from) {
      opener.value = from;
    }
  }

  function close(): void {
    userId.value = null;
  }

  return {
    userId,
    opener,
    open,
    close,
  };
});
