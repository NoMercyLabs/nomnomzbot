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

import { beforeEach, describe, expect, it, vi } from 'vitest';
import { defineComponent } from 'vue';
import { createMemoryHistory, createRouter, type RouteRecordRaw, type Router } from 'vue-router';

import { homeRoute } from '@/views/site/home/route';
import { loginRoute } from '@/views/site/login/route';

import { signInGuard } from '../guard';

const held = vi.hoisted(() => ({ signedIn: true }));

vi.mock('@/api/auth', () => ({
  restoreSession: () => Promise.resolve(held.signedIn),
  takeOAuthLanding: () => false,
}));

const page = defineComponent({ render: () => null });

const routes: RouteRecordRaw[] = [
  {
    path: '/',
    name: 'home',
    meta: homeRoute.meta,
    component: page,
  },
  {
    path: '/login',
    name: loginRoute.name,
    meta: loginRoute.meta,
    component: page,
  },
  {
    path: '/app',
    name: 'app',
    meta: { requiresAuth: true },
    component: page,
  },
  {
    path: '/app/discord',
    name: 'discord',
    meta: { requiresAuth: true },
    component: page,
  },
];

function router(): Router {
  const created = createRouter({ history: createMemoryHistory(), routes });

  created.beforeEach(signInGuard);

  return created;
}

beforeEach(() => {
  held.signedIn = true;
});

describe('a visitor who is already signed in', () => {
  it('skips the home page and lands on the dashboard', async () => {
    const subject = router();

    await subject.push('/');

    expect(subject.currentRoute.value.path).toBe('/app');
  });

  it('skips the login page and lands where the login was asked for', async () => {
    const subject = router();

    await subject.push('/login?return_to=/app/discord');

    expect(subject.currentRoute.value.path).toBe('/app/discord');
  });

  it('still sees the home page when signed out', async () => {
    held.signedIn = false;

    const subject = router();

    await subject.push('/');

    expect(subject.currentRoute.value.path).toBe('/');
  });
});
