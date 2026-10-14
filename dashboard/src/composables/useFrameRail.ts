// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
