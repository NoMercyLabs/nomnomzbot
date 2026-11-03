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

import { computed, type ComputedRef } from 'vue';
import { useRoute, useRouter, type LocationQuery } from 'vue-router';

import { merge } from '@/lib/array';

// A detail pane that lives in the route query (?user=) opens with one history step and closes by popping it,
// as in a native app. Stoney 2026-11-03: "i want EVERYTHING to have a history state, and a close is a history pop".
// Opening while a pane is open replaces, so switching rows in a list does not stack steps. A pane that opened
// from a pasted link has no earlier step of ours, so its close replaces the entry instead of leaving the page.
// `companions` are other keys that belong to the same pane (a tab of the open person, the other kind of
// command): opening drops them and closing removes them with the key.
export interface QueryPane {
  id: ComputedRef<string | null>;
  open: (id: string) => void;
  close: () => void;
}

function omit(query: LocationQuery, keys: readonly string[]): LocationQuery {
  const kept = Object.entries(query).filter(([name]) => !keys.includes(name));

  return Object.fromEntries(kept);
}

function signature(query: LocationQuery): string {
  const entries = Object.entries(query).sort(([a], [b]) => a.localeCompare(b));

  return JSON.stringify(entries);
}

export function useQueryPane(key: string, companions: readonly string[] = []) {
  const route = useRoute();
  const router = useRouter();
  const names = merge([key], companions);

  const id = computed<string | null>(() => {
    const value = route.query[key];

    return typeof value === 'string'
      ? value
      : null;
  });

  function isOpen(): boolean {
    return names.some((name) => typeof route.query[name] === 'string');
  }

  function open(next: string): void {
    const query = { ...omit(route.query, companions), [key]: next };

    if (isOpen()) {
      void router.replace({ query });

      return;
    }

    void router.push({ query });
  }

  // The step before this one is ours only when it is this page without the pane.
  function cameFromPage(rest: LocationQuery): boolean {
    const back = router.options.history.state.back;

    if (typeof back !== 'string')
      return false;

    const earlier = router.resolve(back);

    return earlier.path === route.path && signature(earlier.query) === signature(rest);
  }

  function close(): void {
    const rest = omit(route.query, names);

    if (cameFromPage(rest)) {
      router.back();

      return;
    }

    void router.replace({ query: rest });
  }

  return {
    id,
    open,
    close,
  } as const satisfies QueryPane;
}
