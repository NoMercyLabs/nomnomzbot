// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { VueQueryPlugin } from '@tanstack/vue-query';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia } from 'pinia';
import { afterEach, describe, expect, it, vi } from 'vitest';

import type { SetupStep } from '@/api/setup';

import words from '../i18n/en.json';

const authorizeUrl = 'https://id.twitch.tv/oauth2/authorize?client_id=abc&scope=chat%3Aread';

function setupStep(over: Partial<SetupStep>): SetupStep {
  return {
    key: 'platform_bot',
    title: 'Bot account',
    description: 'd',
    required: false,
    complete: false,
    status: 'x',
    instructions: [],
    action: {
      type: 'oauth_redirect',
      method: 'GET',
      endpoint: '/b',
      pollEndpoint: '/s',
    },
    fields: [],
    useSharedAction: null,
    ...over,
  } as SetupStep;
}

function stubClipboard() {
  const writeText = vi.fn<(text: string) => Promise<void>>(async () => undefined);

  Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });

  return writeText;
}

function stubBotApi() {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (request: Request) => {
      const path = new URL(request.url).pathname;

      if (path.endsWith('/setup/bot/oauth-url'))
        return Response.json({ data: { oauthUrl: authorizeUrl } });

      if (path.endsWith('/setup/bot/status'))
        return Response.json({ data: { connected: false } });

      return Response.json({ data: {} });
    }),
  );
}

async function mountStep(kind: 'credentials' | 'bot', step: SetupStep): Promise<VueWrapper> {
  vi.resetModules();
  const { i18n } = await import('@/i18n');

  i18n.global.mergeLocaleMessage('en', words);
  const { queryClient } = await import('@/api/queryClient');
  const component =
    kind === 'credentials'
      ? (await import('../components/CredentialsStep.vue')).default
      : (await import('../components/BotAccountStep.vue')).default;
  const wrapper: VueWrapper = mount(component, {
    props: { step },
    global: { plugins: [createPinia(), [VueQueryPlugin, { queryClient }], i18n] },
  });

  await flushPromises();

  return wrapper;
}

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('a link in an instruction line', () => {
  it('opens in a new tab through a safe anchor and copies to the clipboard', async () => {
    const writeText = stubClipboard();
    const wrapper = await mountStep(
      'credentials',
      setupStep({
        key: 'twitch_app',
        instructions: ['Open https://dev.twitch.tv/console/apps and sign in.'],
      }),
    );

    const open = wrapper.get('a[href="https://dev.twitch.tv/console/apps"]');

    expect(open.attributes('target')).toBe('_blank');
    expect(open.attributes('rel')).toBe('noopener noreferrer');
    expect(open.text()).toBe('Open in new tab');
    expect(wrapper.text()).toContain('Open');
    expect(wrapper.text()).toContain('and sign in.');

    await wrapper.get('[data-copy-link]').trigger('click');

    expect(writeText).toHaveBeenCalledWith('https://dev.twitch.tv/console/apps');
  });

  it('gives each link of a line its own pair of actions and keeps a trailing period out', async () => {
    stubClipboard();
    const wrapper = await mountStep(
      'credentials',
      setupStep({
        key: 'spotify',
        instructions: ['Use https://a.example/one and https://developer.spotify.com/dashboard.'],
      }),
    );

    const hrefs = wrapper.findAll('a[target="_blank"]').map((a) => a.attributes('href'));

    expect(hrefs).toEqual(['https://a.example/one', 'https://developer.spotify.com/dashboard']);
    expect(wrapper.findAll('[data-copy-link]')).toHaveLength(2);
  });

  it('shows a line without a link as plain text with no actions', async () => {
    const wrapper = await mountStep(
      'credentials',
      setupStep({
        key: 'twitch_app',
        instructions: ['Create an application'],
      }),
    );

    expect(wrapper.text()).toContain('Create an application');
    expect(wrapper.find('a[target="_blank"]').exists()).toBe(false);
    expect(wrapper.find('[data-copy-link]').exists()).toBe(false);
  });
});

describe('the bot account step', () => {
  it('keeps the authorization link on screen, copyable, after Authorize opens it', async () => {
    const writeText = stubClipboard();
    const open = vi.spyOn(window, 'open').mockReturnValue({} as Window);

    stubBotApi();
    const wrapper = await mountStep('bot', setupStep({}));

    expect(wrapper.find('[data-copy-link]').exists()).toBe(false);

    await wrapper.get('button').trigger('click');
    await flushPromises();

    expect(open).toHaveBeenCalledWith(authorizeUrl, '_blank', 'noopener');
    expect(wrapper.text()).toContain('Use another browser or profile? Copy this link there.');
    expect(wrapper.get(`a[href="${authorizeUrl}"]`).attributes('target')).toBe('_blank');

    await wrapper.get('[data-copy-link]').trigger('click');

    expect(writeText).toHaveBeenCalledWith(authorizeUrl);
  });

  it('still shows the link when the browser blocks the popup', async () => {
    stubClipboard();
    vi.spyOn(window, 'open').mockReturnValue(null);

    stubBotApi();
    const wrapper = await mountStep('bot', setupStep({}));

    await wrapper.get('button').trigger('click');
    await flushPromises();

    expect(wrapper.get(`a[href="${authorizeUrl}"]`).text()).toBe('Open in new tab');
    expect(wrapper.find('[data-copy-link]').exists()).toBe(true);
  });
});
