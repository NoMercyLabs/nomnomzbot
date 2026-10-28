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

import { forgetOnboardingAnswer, setupGuard } from '../guard';

const held = vi.hoisted(() => ({
  signedIn: true,
  probe: { state: 'ready', onboardingComplete: false } as
  | { state: 'ready'; onboardingComplete: boolean } |
  { state: 'unreachable' },
}));

const probeBot = vi.hoisted(() => vi.fn<() => Promise<typeof held.probe>>());

vi.mock('@/api/system', () => ({ probeBot }));
vi.mock('@/api/activeBot', () => ({ activeBot: { value: 'https://bot.example' } }));

vi.mock('@/api/auth', () => ({
  restoreSession: () => Promise.resolve(held.signedIn),
  takeOAuthLanding: () => false,
}));

const page = defineComponent({ render: () => null });
const routes: RouteRecordRaw[] = [
  {
    path: '/setup',
    name: 'setup',
    meta: { setup: true },
    component: page,
  },
  {
    path: '/login',
    name: 'login',
    meta: { guestOnly: true },
    component: page,
  },
  {
    path: '/app',
    name: 'app',
    meta: { requiresAuth: true },
    component: page,
  },
  {
    path: '/',
    name: 'home',
    component: page,
  },
];

async function visit(path: string): Promise<string> {
  const router: Router = createRouter({ history: createMemoryHistory(), routes });

  router.beforeEach(setupGuard);
  await router.push(path);

  return router.currentRoute.value.path;
}

beforeEach(() => {
  held.signedIn = true;
  forgetOnboardingAnswer();
  probeBot.mockReset();
  probeBot.mockImplementation(() => Promise.resolve(held.probe));
});

describe('the first-run setup guard', () => {
  it('sends the dashboard to the wizard while onboarding is not complete', async () => {
    held.probe = { state: 'ready', onboardingComplete: false };

    expect(await visit('/app')).toBe('/setup');
  });

  it('sends the login page to the wizard while onboarding is not complete', async () => {
    held.probe = { state: 'ready', onboardingComplete: false };

    expect(await visit('/login')).toBe('/setup');
  });

  it('lets the dashboard open once onboarding is complete', async () => {
    held.probe = { state: 'ready', onboardingComplete: true };

    expect(await visit('/app')).toBe('/app');
  });

  it('sends a signed-in owner from the wizard to the dashboard once onboarding is complete', async () => {
    held.probe = { state: 'ready', onboardingComplete: true };
    held.signedIn = true;

    expect(await visit('/setup')).toBe('/app');
  });

  it('keeps a visitor who is not signed in on the wizard after the Twitch app is chosen', async () => {
    held.probe = { state: 'ready', onboardingComplete: true };
    held.signedIn = false;

    expect(await visit('/setup')).toBe('/setup');
  });

  it('keeps the wizard open while onboarding is not complete', async () => {
    held.probe = { state: 'ready', onboardingComplete: false };

    expect(await visit('/setup')).toBe('/setup');
  });

  it('leaves the public home page alone and asks the bot nothing', async () => {
    held.probe = { state: 'ready', onboardingComplete: false };

    expect(await visit('/')).toBe('/');
    expect(probeBot).not.toHaveBeenCalled();
  });

  it('does not block a bot that does not answer', async () => {
    held.probe = { state: 'unreachable' };

    expect(await visit('/app')).toBe('/app');
  });

  it('asks the bot only once after it reports onboarding complete', async () => {
    held.probe = { state: 'ready', onboardingComplete: true };

    await visit('/app');
    await visit('/login');

    expect(probeBot).toHaveBeenCalledTimes(1);
  });
});
