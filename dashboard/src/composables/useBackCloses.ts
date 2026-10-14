// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// On a phone every open menu, dialog, drawer and picker is a step in the history, as in a native app: the back
// button closes the top one instead of leaving the page. Stoney 2026-10-14: "Can we capture all menu's as
// navigation and closes as history back for mobile for the native feeling?"
// Every reka layer carries [data-dismissable-layer], so one watcher here covers them all.
// A layer closed by a tap never moves the history itself: a history move then races the navigation or the send
// that closed it ("Clicking send redirects after sending"). Its step stays behind as a dead step instead; the
// next page replaces it, and a back press that lands on one keeps going.
// Chrome's back button skips an entry added without a user gesture, so a step is only added inside one.
import {
  tryOnScopeDispose,
  useDebounceFn,
  useEventListener,
  useMediaQuery,
  useMutationObserver,
} from '@vueuse/core';
import { watch, type Ref } from 'vue';
import { useRouter } from 'vue-router';

const openLayers = '[data-dismissable-layer]:not([data-state="closed"])';

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

  watch(open, (isOpen: boolean) => {
    leave();

    if (isOpen) {
      panels.push(close);
    }
  }, { immediate: true });

  tryOnScopeDispose(leave);
}

function rekaLayerCount(): number {
  return [...document.querySelectorAll(openLayers)].filter((layer) =>
    layer.querySelector('[role="tooltip"]') === null).length;
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

// A reka layer opens over the page, so it is above any panel and closes first.
function closeTopLayer(): void {
  if (rekaLayerCount() > 0) {
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));

    return;
  }

  panels.at(-1)?.();
}

export function useBackCloses(): void {
  const phone = useMediaQuery('(width < 48rem)');
  const router = useRouter();
  let navigating = false;
  let popping = false;
  let lastPosition = position();
  let lastPage = page();
  // The entry on screen holds the step of a layer that a tap already closed.
  let onDeadStep = false;

  const stopBefore = router.beforeEach((to, from) => {
    navigating = true;

    const leaving = to.path !== from.path && !to.redirectedFrom;

    if (popping || !phone.value || steps() === 0 || !leaving) return;

    // Leaving the page: the new page takes the place of the layer step, so back returns to the page itself.
    return {
      path: to.path,
      query: to.query,
      hash: to.hash,
      replace: true,
    };
  });

  // The entry the person stands on, read once the router has written it.
  function remember(): void {
    lastPosition = position();
    lastPage = page();
  }

  const stopAfter = router.afterEach(() => {
    navigating = false;
    popping = false;
    setTimeout(remember, 0);
  });

  tryOnScopeDispose(() => {
    stopBefore();
    stopAfter();
  });

  const reconcile = useDebounceFn(async (): Promise<void> => {
    if (!phone.value || navigating) return;

    const open = layerCount();
    const current = steps();

    onDeadStep = current > open;

    if (open <= current) return;

    // A select or a message menu opens on the finger going down, before the tap counts as a gesture. The
    // back button skips a step added without one, so the step waits for the finger to lift.
    if (!gesture()) {
      window.addEventListener('pointerup', reconcile, { once: true, capture: true });

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

    const wentBack = position() < lastPosition && page() === lastPage;
    const leftDeadStep = onDeadStep;

    lastPosition = position();
    onDeadStep = false;

    if (!phone.value || !wentBack) return;

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
