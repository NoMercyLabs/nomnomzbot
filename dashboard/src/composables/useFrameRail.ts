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

// A part of the page can ask the frame to fold its sidebar to the icon rail while it needs the room. The frame
// folds while any part asks, and unfolds when the last one lets go.
import { computed, onScopeDispose, ref, watch, type Ref } from 'vue';

const requests = ref(0);

export const railWanted = computed(() =>
  requests.value > 0);

export function useFrameRail(wanted: Readonly<Ref<boolean>>): void {
  let holding = false;

  function hold(on: boolean): void {
    if (on === holding) return;

    holding = on;

    requests.value += on
      ? 1
      : -1;
  }

  watch(wanted, hold, { immediate: true });

  onScopeDispose(() =>
    hold(false));
}
