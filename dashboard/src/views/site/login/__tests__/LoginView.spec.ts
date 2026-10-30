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

import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia } from 'pinia';
import { VueQueryPlugin } from '@tanstack/vue-query';
import { siTwitch } from 'simple-icons';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import words from '../i18n/en.json';

const asked = vi.hoisted(() => ({ query: {} as Record<string, string> }));

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: asked.query }),
  useRouter: () => ({ replace: vi.fn<() => Promise<void>>() }),
}));

const official: string = 'https://nomnomz.bot';
const twitch = {
  key: 'twitch',
  displayName: 'Twitch',
  flows: ['redirect', 'device_code'],
  enabled: true,
};
const code = {
  deviceCode: 'dc',
  userCode: 'ABCD-EFGH',
  verificationUri: 'https://www.twitch.tv/activate',
  interval: 5,
  expiresIn: 600,
};

function stubBot(): string[] {
  const calls: string[] = [];

  vi.stubGlobal(
    'fetch',
    vi.fn(async (request: Request) => {
      calls.push(`${request.method} ${request.url}`);
      const path = new URL(request.url).pathname;

      if (path.endsWith('/auth/providers'))
        return Response.json({ data: [twitch] });

      if (path.endsWith('/auth/twitch/device'))
        return Response.json({ data: code });

      return Response.json({ data: { status: 'pending' } });
    }),
  );

  return calls;
}

const modules = {
  i18n: () => import('@/i18n'),
  queryClient: () => import('@/api/queryClient'),
  loginView: () => import('../LoginView.vue'),
} as const;

// Each mount starts the modules over (vi.resetModules), but the first import of each is a cold transform of the
// page's graph (3 s alone, far more beside a build). It happens while the file loads, where no test timeout
// runs; the mounts then only evaluate what is already transformed.
await Promise.all(Object.values(modules).map((load) => load()));

async function mountLogin(): Promise<VueWrapper> {
  vi.resetModules();
  const { i18n } = await modules.i18n();

  i18n.global.mergeLocaleMessage('en', words);
  const { queryClient } = await modules.queryClient();
  const LoginView = (await modules.loginView()).default;
  const wrapper: VueWrapper = mount(LoginView, {
    global: { plugins: [createPinia(), [VueQueryPlugin, { queryClient }], i18n] },
  });

  await flushPromises();

  return wrapper;
}

beforeEach(() => {
  localStorage.clear();
  asked.query = {};
});

describe('signing in by role', () => {
  it.each([
    ['Continue as broadcaster', 'broadcaster'],
    ['Continue as moderator', 'moderator'],
    ['Continue as viewer', 'viewer'],
  ])('%s starts Twitch with the %s role and returns to the page asked for', async (label, role) => {
    asked.query = { return_to: '/me/stoney_eagle' };
    vi.stubGlobal('isSecureContext', true);
    const assign = vi.fn<(url: string) => void>();

    vi.stubGlobal('location', { ...window.location, assign });
    stubBot();
    const wrapper = await mountLogin();
    const button = wrapper.findAll('button').find((b) => b.text() === label);

    await button!.trigger('click');

    const url = new URL(assign.mock.calls[0]![0]);

    expect(url.pathname).toBe('/api/v1/auth/twitch');
    expect(url.searchParams.get('client')).toBe('web');
    expect(url.searchParams.get('role')).toBe(role);
    expect(url.searchParams.get('return_to')).toBe('/me/stoney_eagle');
  });
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.useRealTimers();
});

// Stoney 2026-10-16: "Login as mod and user did not work and now I'm ratelimited". The page said no login
// was set up while the bot answered 429.
describe('a bot that answers the sign-in list with 429', () => {
  function stubProviders(respond: () => Response): void {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => respond()),
    );
  }

  it('says when to try again, counts down, and never says no sign-in is set up', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    stubProviders(() => new Response('{}', { status: 429, headers: { 'Retry-After': '30' } }));
    const wrapper = await mountLogin();

    expect(wrapper.text()).toContain('Too many sign-in tries. Try again in 30 seconds.');
    expect(wrapper.text()).not.toContain('no sign-in switched on');

    const retry = wrapper.findAll('button').find((b) => b.text() === 'Try again');

    expect(retry!.attributes('disabled')).toBeDefined();

    await vi.advanceTimersByTimeAsync(10_000);

    expect(wrapper.text()).toContain('Try again in 20 seconds.');

    await vi.advanceTimersByTimeAsync(20_000);
    await flushPromises();

    const ready = wrapper.findAll('button').find((b) => b.text() === 'Try again');

    expect(ready!.attributes('disabled')).toBeUndefined();
  });

  it('counts 60 seconds when the bot sends no Retry-After', async () => {
    stubProviders(() => new Response('{}', { status: 429 }));
    const wrapper = await mountLogin();

    // The client waits one second twice (its default) before it hands the 429 to the page.
    await vi.waitFor(() => expect(wrapper.text()).toContain('Try again in 6'), { timeout: 5000 });

    expect(wrapper.text()).toMatch(/Try again in (60|59) seconds\./);
  });

  it('says no sign-in is set up only when the list really came back empty', async () => {
    stubProviders(() => Response.json({ data: [] }));
    const wrapper = await mountLogin();

    expect(wrapper.text()).toContain('no sign-in switched on');
    expect(wrapper.text()).not.toContain('Too many sign-in tries');
  });
});

// A browser sign-in returns to the bot's own site, so another bot is signed in to with a code
// (Stoney 2026-10-13, translated from Dutch: "I must be able to use the SaaS with my own local client").
describe('signing in to a bot this page does not belong to', () => {
  it('names that bot and starts a code sign-in on it, not a redirect', async () => {
    localStorage.setItem('nnz.bot', official);
    const calls = stubBot();
    const wrapper = await mountLogin();

    expect(wrapper.text()).toContain('nomnomz.bot');
    expect(wrapper.text()).toContain('From here, you sign in with a code.');
    expect(wrapper.text()).not.toContain('Sign in with a code instead');

    await wrapper
      .findAll('button')
      .find((b) => b.text() === 'Continue with Twitch')!
      .trigger('click');

    await flushPromises();

    expect(calls).toContain(`POST ${official}/api/v1/auth/twitch/device`);
    expect(wrapper.text()).toContain('ABCD-EFGH');
  });
});

// Twitch and Google only return to https or localhost. Stoney 2026-10-13 (translated from Dutch), on his phone at 192.168.2.60:
// "I am being redirected to localhost again".
describe('signing in from a plain http address on the network', () => {
  it('uses a code for the bot this page belongs to, because no platform will return to that address', async () => {
    vi.stubGlobal('isSecureContext', false);
    const calls = stubBot();
    const wrapper = await mountLogin();

    expect(wrapper.text()).toContain('On this address, you sign in with a code.');
    expect(wrapper.text()).not.toContain('has its own sign-in page');

    await wrapper
      .findAll('button')
      .find((b) => b.text() === 'Continue with Twitch')!
      .trigger('click');

    await flushPromises();

    expect(
      calls.some((call) => call.startsWith('POST ') && call.endsWith('/api/v1/auth/twitch/device')),
    ).toBe(true);

    expect(wrapper.text()).toContain('ABCD-EFGH');
  });
});

// Stoney 2026-10-13 (translated from Dutch): "the login button must still get the logo of the platform, neatly. There must be a
// branding with a welcome and a continue. And a condition (terms) must be added."
describe('the sign-in options', () => {
  it('welcome the visitor, give each platform its own logo, and state the terms', async () => {
    stubBot();
    const wrapper = await mountLogin();

    expect(wrapper.find('h1').text()).toMatch(/NomNomzBot$/);
    expect(wrapper.find('h2').text()).toBe('Welcome');
    const button = wrapper.findAll('button').find((b) => b.text() === 'Continue with Twitch')!;

    expect(button.find('svg path').attributes('d')).toBe(siTwitch.path);
    expect(button.find('svg').attributes('aria-hidden')).toBe('true');

    expect(wrapper.text()).toContain(
      'By continuing, you agree to the terms of service and the privacy policy.',
    );
  });
});
