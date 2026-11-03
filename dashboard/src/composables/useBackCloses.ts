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

// Every open menu, dialog, drawer and picker is a step in the history on every width, as in a native app: the
// back button closes the top one instead of leaving the page, and a close that did not come from the back
// button pops its step. Stoney 2026-10-14: "Can we capture all menu's as navigation and closes as history back
// for mobile for the native feeling?" Stoney 2026-11-03: "i want EVERYTHING to have a history state, and a close
// is a history pop".
// Every reka layer carries [data-dismissable-layer], so one watcher here covers them all.
// A pop races a navigation that the same tap caused ("Clicking send redirects after sending"), so a navigation
// that starts while a pop is pending waits for the pop to land, and one that starts on a layer step replaces it.
// Chrome's back button skips an entry added without a user gesture, so a step is only added inside one.
import {
  tryOnScopeDispose,
  useDebounceFn,
  useEventListener,
  useMutationObserver,
} from '@vueuse/core';
import { watch, type Ref } from 'vue';
import { useRouter } from 'vue-router';

const openLayers = '[data-dismissable-layer]:not([data-state="closed"])';

// A pop that never raises its popstate (nothing to go back to) must not hold a navigation forever.
const popTimeout = 300;

// Panels that are not reka layers (a suggestion list, a reply bar, a full-screen editor), newest last.
const panels: (() => void)[] = [];

// A panel that is not a reka layer signs up here while it is open, so back closes it like any menu.
export function useBackLayer(open: Readonly<Ref<boolean>>, close: () => void): void {
  function leave(): void {
    const index = panels.indexOf(close);

    if (index >= 0) {
      panels.splice(index, 1);
    }
  }

  watch(
    open,
    (isOpen: boolean) => {
      leave();

      if (isOpen) {
        panels.push(close);
      }
    },
    { immediate: true },
  );

  tryOnScopeDispose(leave);
}

function rekaLayerCount(): number {
  return [...document.querySelectorAll(openLayers)].filter(
    (layer) => layer.querySelector('[role="tooltip"]') === null,
  ).length;
}

function layerCount(): number {
  return rekaLayerCount() + panels.length;
}

// How many layer steps the current history entry stands on.
function steps(): number {
  return Number(window.history.state?.backLayers ?? 0);
}

// The router numbers its history entries, so a lower number after a pop means the person went back.
function position(): number {
  return Number(window.history.state?.position ?? 0);
}

function page(): string {
  return `${window.location.pathname}${window.location.search}`;
}

// A browser without the activation API counts every moment as a gesture.
function gesture(): boolean {
  return navigator.userActivation?.isActive ?? true;
}

// Runs once when the finger lifts: a pointerup, or a touchend alone after a long press. Whichever comes first
// removes both, so no stale listener runs on a later touch.
function afterLift(run: () => void): AbortController {
  const lifted = new AbortController();

  const once = (): void => {
    lifted.abort();
    run();
  };

  window.addEventListener('pointerup', once, { capture: true, signal: lifted.signal });
  window.addEventListener('touchend', once, { capture: true, signal: lifted.signal });

  return lifted;
}

// A reka layer opens over the page, so it is above any panel and closes first.
function closeTopLayer(): void {
  if (rekaLayerCount() > 0) {
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));

    return;
  }

  panels.at(-1)?.();
}

export function useBackCloses(): void {
  const router = useRouter();
  let navigating = false;
  let popping = false;
  let lastPosition = position();
  let lastPage = page();
  // The entry on screen holds the step of a layer that closed without a pop (the fallback).
  let onDeadStep = false;
  // Layers open at the last check: a drop below it is a close, and a close pops its step.
  let lastOpen = 0;
  // The pop of ours in flight. A navigation waits on it; the popstate it raises ends it.
  let pendingPop: Promise<void> | null = null;
  let landPop: (() => void) | null = null;

  function startPop(count: number): Promise<void> {
    const popped = new Promise<void>((resolve) => {
      const timer = setTimeout(() => landPop?.(), popTimeout);

      landPop = () => {
        clearTimeout(timer);
        landPop = null;
        pendingPop = null;
        resolve();
      };
    });

    pendingPop = popped;
    window.history.go(-count);

    return popped;
  }

  const stopBefore = router.beforeEach(async (to, from) => {
    navigating = true;

    // The pop moves the entry under the navigation, so the navigation restarts on the clean entry once it landed.
    if (pendingPop) {
      await pendingPop;

      return {
        path: to.path,
        query: to.query,
        hash: to.hash,
      };
    }

    const leaving = to.fullPath !== from.fullPath && !to.redirectedFrom;

    if (popping || steps() === 0 || !leaving) return;

    // Leaving the page: the new page takes the place of the layer step, so back returns to the page itself.
    return {
      path: to.path,
      query: to.query,
      hash: to.hash,
      replace: true,
      // The router merges the old state into a replaced entry, so the step count is cleared by hand.
      state: { backLayers: 0 },
    };
  });

  // The entry the person stands on, read once the router has written it.
  function remember(): void {
    lastPosition = position();
    lastPage = page();
    lastOpen = layerCount();
  }

  const stopAfter = router.afterEach(() => {
    navigating = false;
    popping = false;

    setTimeout(() => {
      remember();
      void reconcile();
    }, 0);
  });

  // The wait for the finger to lift, while a layer opened before the tap counted.
  let lift: AbortController | null = null;

  tryOnScopeDispose(() => {
    stopBefore();
    stopAfter();
    lift?.abort();
  });

  const reconcile = useDebounceFn(async (): Promise<void> => {
    if (navigating) return;

    if (pendingPop) {
      await pendingPop;
      void reconcile();

      return;
    }

    const open = layerCount();
    const current = steps();
    const closed = lastOpen > open && current > open;

    lastOpen = open;
    onDeadStep = current > open;

    // A close that did not come from Back: the layers that went away take their steps with them.
    if (closed) {
      await startPop(current - open);
      onDeadStep = steps() > layerCount();

      return;
    }

    if (open <= current) return;

    // A select or a message menu opens on the finger going down, before the tap counts as a gesture. The
    // back button skips a step added without one, so the step waits for the finger to lift. A long press
    // (the emote tone picker) lifts with a touchend only: the browser cancels its pointer.
    if (!gesture()) {
      lift?.abort();
      lift = afterLift(reconcile);

      return;
    }

    const { path, query, hash } = router.currentRoute.value;

    await router.push({
      path,
      query,
      hash,
      force: true,
      state: { backLayers: current + 1 },
    });

    remember();
  }, 0);

  useEventListener(window, 'popstate', () => {
    popping = true;

    // The popstate of our own pop: it closes no layer and leaves no page.
    if (landPop) {
      lastPosition = position();
      onDeadStep = false;
      landPop();

      return;
    }

    const wentBack = position() < lastPosition && page() === lastPage;
    const leftDeadStep = onDeadStep;

    lastPosition = position();
    onDeadStep = false;

    if (!wentBack) return;

    if (layerCount() > 0) {
      closeTopLayer();

      return;
    }

    // Nothing was left to close, so this press would change nothing on screen: it means "leave", so keep going.
    if (leftDeadStep || steps() > 0) {
      router.go(-1);
    }
  });

  useMutationObserver(document.body, reconcile, {
    childList: true,
    subtree: true,
    attributes: true,
    attributeFilter: ['data-state'],
  });
}
