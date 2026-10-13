// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'

// Which person the one profile dialog shows. A name anywhere in the dashboard calls open();
// a second call swaps the person, it never makes a second dialog. The opener gets the focus back on close.
export const useProfileDialogStore = defineStore('profileDialog', () => {
  const userId: Ref<string | null> = ref(null)
  const opener: Ref<HTMLElement | null> = ref(null)

  function open(id: string, from: HTMLElement | null = null): void {
    userId.value = id
    if (from) opener.value = from
  }

  function close(): void {
    userId.value = null
  }

  return { userId, opener, open, close }
})
