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

// Stoney 2026-11-03: "i want EVERYTHING to have a history state, and a close is a history pop". A page under test
// mounts inside BackHost, which runs the real watcher on the real router and the real history.
import { flushPromises } from '@vue/test-utils';
import { expect } from 'vitest';
import { defineComponent } from 'vue';
import { createRouter, createWebHistory, type Router } from 'vue-router';

import { useBackCloses } from '@/composables/useBackCloses';

export const BackHost = defineComponent({
  setup(_, { slots }) {
    useBackCloses();

    return () =>
      slots.default?.();
  },
});

export async function settle(): Promise<void> {
  await flushPromises();

  await new Promise((resolve) => {
    setTimeout(resolve, 40);
  });

  await flushPromises();
}

export function position(): number {
  return window.history.state?.position;
}

export function steps(): number {
  return window.history.state?.backLayers ?? 0;
}

// Two entries deep on /chat, so a Back that leaves the page would show /home.
export async function startHistory(): Promise<Router> {
  window.history.replaceState(null, '', '/');

  const router = createRouter({
    history: createWebHistory(),
    routes: [{ path: '/:any(.*)*', component: { render: () => null } }],
  });

  await router.push('/home');
  await router.push('/chat');

  return router;
}

// The surface just opened: one step on top of where the page stood, and the route is the same.
export function expectStepAdded(router: Router, start: number): void {
  expect(position()).toBe(start + 1);
  expect(steps()).toBe(1);
  expect(router.currentRoute.value.fullPath).toBe('/chat');
}

// The surface is closed and its step is gone: the page stands where it started and Back still leaves it.
export async function expectStepPopped(router: Router, start: number): Promise<void> {
  expect(position()).toBe(start);
  expect(steps()).toBe(0);
  expect(router.currentRoute.value.fullPath).toBe('/chat');

  window.history.back();
  await settle();
  expect(router.currentRoute.value.fullPath).toBe('/home');
}

export async function pressBack(): Promise<void> {
  window.history.back();
  await settle();
}
