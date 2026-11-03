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

// Stoney 2026-11-03: "i want EVERYTHING to have a history state, and a close is a history pop". The bot list and
// the code sign-in replace the sign-in options in place, so each takes a step and Back returns to the options.
import { VueQueryPlugin } from '@tanstack/vue-query';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { h } from 'vue';

import {
  BackHost,
  expectStepAdded,
  expectStepPopped,
  position,
  pressBack,
  settle,
  startHistory,
} from '@/__tests__/backHistory';
import { queryClient } from '@/api/queryClient';
import { i18n } from '@/i18n';

import words from '../i18n/en.json';
import LoginView from '../LoginView.vue';

let mounted: VueWrapper | undefined;

// The API client reads fetch when its module loads, so the bot's answers stand in before the page is imported.
vi.hoisted(() => {
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

  globalThis.fetch = (async (request: Request) => {
    const path = new URL(request.url).pathname;

    if (path.endsWith('/auth/providers'))
      return Response.json({ data: [twitch] });

    if (path.endsWith('/auth/twitch/device'))
      return Response.json({ data: code });

    return Response.json({ data: { status: 'pending' } });
  }) as typeof fetch;
});

beforeEach(() => {
  localStorage.clear();
  queryClient.clear();
  i18n.global.mergeLocaleMessage('en', words);
});

afterEach(() => {
  mounted?.unmount();
  mounted = undefined;
  document.body.innerHTML = '';
  vi.unstubAllGlobals();
});

async function openLogin(secure: boolean) {
  vi.stubGlobal('isSecureContext', secure);

  const router = await startHistory();

  mounted = mount(BackHost, {
    attachTo: document.body,
    global: { plugins: [router, createPinia(), [VueQueryPlugin, { queryClient }], i18n] },
    slots: {
      default: () =>
        h(LoginView),
    },
  });

  await router.isReady();

  await vi.waitFor(() =>
    expect(document.body.textContent).not.toContain(words.site.login.loading));

  await settle();

  return { router, start: position() };
}

function buttonNamed(label: string): HTMLButtonElement | undefined {
  return [...document.body.querySelectorAll('button')].find((button) =>
    button.textContent?.includes(label));
}

const optionsShown = (): boolean =>
  buttonNamed(words.site.login.change) !== undefined;

describe('the sign-in page in the history', () => {
  it('takes a step for the bot list and Back returns to the options on the same route', async () => {
    const { router, start } = await openLogin(true);

    buttonNamed(words.site.login.change)!.click();
    await settle();
    expect(optionsShown()).toBe(false);
    expectStepAdded(router, start);

    await pressBack();
    expect(optionsShown()).toBe(true);
    expect(position()).toBe(start);
    expect(router.currentRoute.value.fullPath).toBe('/chat');
  });

  it('pops the step when the bot list closes with its own back button', async () => {
    const { router, start } = await openLogin(true);

    buttonNamed(words.site.login.change)!.click();
    await settle();
    expectStepAdded(router, start);

    buttonNamed(words.site.login.bots.back)!.click();
    await settle();
    expect(optionsShown()).toBe(true);
    await expectStepPopped(router, start);
  });

  it('takes a step for the code sign-in and Back returns to the options', async () => {
    const { router, start } = await openLogin(true);

    buttonNamed(words.site.login.useCode)!.click();
    await settle();
    expect(optionsShown()).toBe(false);
    expectStepAdded(router, start);

    await pressBack();
    expect(optionsShown()).toBe(true);
    expect(position()).toBe(start);
    expect(router.currentRoute.value.fullPath).toBe('/chat');
  });

  it('pops the step when the code sign-in closes with its own cancel button', async () => {
    const { router, start } = await openLogin(true);

    buttonNamed(words.site.login.useCode)!.click();
    await settle();
    expectStepAdded(router, start);

    buttonNamed(words.site.login.device.back)!.click();
    await settle();
    expect(optionsShown()).toBe(true);
    await expectStepPopped(router, start);
  });

  it('takes no step while the options show', async () => {
    const { start } = await openLogin(true);

    await flushPromises();
    expect(position()).toBe(start);
  });
});
