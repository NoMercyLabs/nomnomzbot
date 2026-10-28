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

import { useQueryClient } from '@tanstack/vue-query';
import { useEventListener, useTimeoutFn } from '@vueuse/core';
import { watch } from 'vue';
import { useRouter, type RouteLocationResolved, type RouteLocationRaw } from 'vue-router';

import { prefetchProfile, prefetchRouteData } from '@/api/prefetch';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

// The pages a person most likely opens next from anywhere; they load while the browser is quiet.
const likelyNext = [
  { name: 'dashboard-chat' },
  { name: 'dashboard-people' },
] as const satisfies readonly RouteLocationRaw[];
const idleDelayMs = 2_500;
const intentEvents = ['pointerover', 'focusin', 'touchstart'];

// A lazy route component is a bare function; a ready component carries one of these marks (as vue-router checks).
function isLazyLoader(component: unknown): component is () => Promise<unknown> {
  return typeof component === 'function' && !('__vccOpts' in component) && !('displayName' in component);
}

// Start the code download of every lazy page a location needs. The router keeps the result for the real visit.
function loadChunks(location: RouteLocationResolved): void {
  for (const record of location.matched) {
    const loader = record.components?.default;

    if (isLazyLoader(loader)) {
      void loader();
    }
  }
}

// Warm a page before it opens: its code chunk and the queries it runs first, read with the page's own query options.
// A link, a name or a quiet moment is the cue: pointer, focus, touch, or the idle timer for the likely next pages.
export function useIntentPrefetch(): void {
  const router = useRouter();
  const queries = useQueryClient();
  const { channelId } = useCurrentChannel();
  const chunkLoaded = new Set<string>();

  function warmRoute(location: RouteLocationRaw): void {
    const target = router.resolve(location);
    const name = String(target.name ?? '');

    if (!chunkLoaded.has(target.fullPath)) {
      chunkLoaded.add(target.fullPath);
      loadChunks(target);
    }

    if (channelId.value !== null) {
      void prefetchRouteData(queries, name, channelId.value);
    }
  }

  function warmTarget(event: Event): void {
    if (!(event.target instanceof Element)) return;

    const person = event.target.closest<HTMLElement>('[data-user]');

    if (person?.dataset.user && channelId.value !== null) {
      void prefetchProfile(queries, channelId.value, person.dataset.user);

      return;
    }

    const href = event.target.closest('a[href^="/"]')?.getAttribute('href');

    if (href) {
      warmRoute(href);
    }
  }

  useEventListener(document, intentEvents, warmTarget, { passive: true });

  const idle = useTimeoutFn(() => likelyNext.forEach(warmRoute), idleDelayMs, { immediate: false });

  watch(
    channelId,
    (id) => {
      if (id !== null) {
        idle.start();
      }
    },
    { immediate: true },
  );
}
