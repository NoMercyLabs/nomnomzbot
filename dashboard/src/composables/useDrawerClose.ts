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

import { computed, inject, onUnmounted, provide, type ComputedRef, type InjectionKey, type Ref } from 'vue';

// A drawer hides the sheet's corner X, so its top panel carries the close button in its own header,
// after its other actions (owner 2026-10-14: "no close button?"). Only the first panel claims it.
// Beside the content the panel closes from the button that opened it, so only a drawer shows the X.
interface DrawerClose {
  close: () => void;
  drawer: Ref<boolean>;
  claimed: boolean;
}

const key: InjectionKey<DrawerClose> = Symbol('drawer-close');

export function provideDrawerClose(close: () => void, drawer: Ref<boolean>): void {
  provide(key, {
    close,
    drawer,
    claimed: false,
  });
}

export function useDrawerClose() {
  const parent = inject(key, undefined);
  const owns = !!parent && !parent.claimed;

  if (parent && owns) {
    parent.claimed = true;

    onUnmounted(() => {
      parent.claimed = false;
    });
  }

  return {
    shown: computed(() =>
      owns && !!parent?.drawer.value),
    close: () => parent?.close(),
  } as const satisfies { shown: ComputedRef<boolean>; close: () => void };
}
