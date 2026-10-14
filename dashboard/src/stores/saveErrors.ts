// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { defineStore } from 'pinia';
import { ref } from 'vue';

// The change a failed save tried to make, in words: a key under frame.inbox.what and its values.
export type SaveWhat = {
  key: string;
  params?: Record<string, string>;
};

export type SaveError = {
  id: string;
  titleKey: string;
  messageKey: string;
  // Stoney 2026-10-14: "it does not tell me what and where so this is kinda useless".
  what: SaveWhat | null;
  // The page the change was made on, so the inbox can name it and lead back to it.
  where: string;
  detectedAt: string;
};

// Saves that failed in this browser. The error inbox lists them beside the items the server reports.
export const useSaveErrorsStore = defineStore('saveErrors', () => {
  const items = ref<SaveError[]>([]);

  function report(
    titleKey: string,
    messageKey: string,
    what: SaveWhat | null = null,
    where = `${location.pathname}${location.search}`,
  ): void {
    items.value.push({
      id: crypto.randomUUID(),
      titleKey,
      messageKey,
      what,
      where,
      detectedAt: new Date().toISOString(),
    });
  }

  function dismiss(id: string): void {
    items.value = items.value.filter((item) => item.id !== id);
  }

  return {
    items,
    report,
    dismiss,
  };
});
