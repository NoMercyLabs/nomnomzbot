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

import type { JsonValue } from '@/lib/json';
import { useLocalStorage } from '@vueuse/core';
import { defineStore } from 'pinia';
import { computed } from 'vue';

import { merge } from '@/lib/array';
import { officialOrigin, servedOrigin } from '@/lib/botAddress';

const alwaysListed = [servedOrigin, officialOrigin] as const satisfies readonly string[];

function readOrigins(raw: string): string[] {
  try {
    const parsed: JsonValue = JSON.parse(raw);

    return Array.isArray(parsed) && parsed.every((item) => typeof item === 'string')
      ? parsed
      : [];
  } catch {
    return [];
  }
}

// The bots this browser has reached, so the visitor can go back without typing the address again.
export const useConnectionsStore = defineStore('connections', () => {
  const saved = useLocalStorage<string[]>('nnz.bots', [], {
    serializer: { read: readOrigins, write: (origins) => JSON.stringify(origins) },
  });

  const bots = computed<string[]>(() => [
    ...new Set(merge(alwaysListed, saved.value)),
  ]);

  function isAlwaysListed(origin: string): boolean {
    return alwaysListed.includes(origin);
  }

  function remember(origin: string): void {
    if (isAlwaysListed(origin)) return;

    saved.value = [origin, ...saved.value.filter((known) => known !== origin)];
  }

  function forget(origin: string): void {
    saved.value = saved.value.filter((known) => known !== origin);
  }

  return {
    bots,
    isAlwaysListed,
    remember,
    forget,
  };
});
