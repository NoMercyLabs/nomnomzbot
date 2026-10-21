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

import words from '../i18n/en.json';

const replace = vi.hoisted(() => vi.fn<(to: string) => Promise<void>>());

vi.mock('vue-router', () => ({
  useRouter: () => ({ replace }),
  useRoute: () => ({ query: {} }),
}));

type Step = {
  key: string;
  title: string;
  description: string;
  required: boolean;
  complete: boolean;
  status: string;
  instructions: string[];
  action: {
    type: string;
    method: string;
    endpoint: string;
    pollEndpoint: string | null;
  };
  fields: Array<{
    key: string;
    label: string;
    type: string;
    required: boolean;
    help: string | null;
  }>;
  useSharedAction: null;
};

function step(key: string, over: Partial<Step>): Step {
  return {
    key,
    title: `${key} title`,
    description: `${key} description`,
    required: false,
    complete: false,
    status: 'x',
    instructions: [],
    action: {
      type: 'save_credentials',
      method: 'PUT',
      endpoint: '/x',
      pollEndpoint: null,
    },
    fields: [],
    useSharedAction: null,
    ...over,
  };
}

const idField = {
  key: 'clientId',
  label: 'Client ID',
  type: 'text',
  required: true,
  help: null,
};

function stubBot(steps: Step[]): string[] {
  const calls: string[] = [];

  vi.stubGlobal(
    'fetch',
    vi.fn(async (request: Request) => {
      const path = new URL(request.url).pathname;

      calls.push(`${request.method} ${path}`);

      if (path.endsWith('/system/setup/wizard'))
        return Response.json({ data: { complete: false, steps } });

      return Response.json({ data: {} });
    }),
  );

  return calls;
}

async function mountWizard(): Promise<VueWrapper> {
  vi.resetModules();
  const { i18n } = await import('@/i18n');

  i18n.global.mergeLocaleMessage('en', words);
  const { queryClient } = await import('@/api/queryClient');
  const SetupView = (await import('../SetupView.vue')).default;
  const wrapper: VueWrapper = mount(SetupView, {
    global: { plugins: [createPinia(), [VueQueryPlugin, { queryClient }], i18n] },
  });

  await flushPromises();

  return wrapper;
}

function button(wrapper: VueWrapper, label: string) {
  return wrapper.findAll('button').find((b) => b.text() === label);
}

afterEach(() => {
  vi.unstubAllGlobals();
  replace.mockReset();
});

describe('the setup wizard', () => {
  it('draws one step per step the server lists, then the finish step', async () => {
    stubBot([
      step('twitch_app', { required: true, fields: [idField] }),
      step('platform_bot', { action: {
        type: 'oauth_redirect',
        method: 'GET',
        endpoint: '/b',
        pollEndpoint: '/s',
      } }),
    ]);

    const wrapper = await mountWizard();

    expect(wrapper.text()).toContain('twitch_app title');
    expect(wrapper.text()).toContain('Step 1 of 3');
  });

  it('pins the page to the top while the steps show, so nothing moves between steps', async () => {
    stubBot([step('twitch_app', { required: true, fields: [idField] })]);
    const wrapper = await mountWizard();
    const classes = wrapper.get('main').classes();

    expect(classes).not.toContain('justify-center');
    expect(classes).toContain('justify-start');
  });

  it('centers the page only while it loads', async () => {
    vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>(() => undefined)));
    const wrapper = await mountWizard();

    expect(wrapper.get('main').classes()).toContain('justify-center');
  });

  it('blocks Next while a required step is not complete', async () => {
    stubBot([step('twitch_app', { required: true, fields: [idField] })]);
    const wrapper = await mountWizard();

    expect(button(wrapper, 'Next')!.attributes('disabled')).toBeDefined();
  });

  it('lets the person skip the optional bot account step and reach the finish step', async () => {
    stubBot([
      step('twitch_app', {
        required: true,
        complete: true,
        fields: [idField],
      }),
      step('platform_bot', {
        title: 'Authorize the bot account (optional)',
        action: {
          type: 'oauth_redirect',
          method: 'GET',
          endpoint: '/b',
          pollEndpoint: '/s',
        },
      }),
    ]);

    const wrapper = await mountWizard();

    await button(wrapper, 'Next')!.trigger('click');

    expect(wrapper.text()).toContain('Authorize the bot account (optional)');

    const skip = button(wrapper, 'Skip');

    expect(skip).toBeDefined();
    expect(skip!.attributes('disabled')).toBeUndefined();
    expect(skip!.attributes('data-variant')).toBe('outline');

    await skip!.trigger('click');

    expect(wrapper.text()).toContain('Finish setup');
  });

  it('calls setup/complete from the finish step and then leaves for the dashboard', async () => {
    const calls = stubBot([step('twitch_app', {
      required: true,
      complete: true,
      fields: [idField],
    })]);
    const wrapper = await mountWizard();

    await button(wrapper, 'Next')!.trigger('click');
    await button(wrapper, 'Finish setup')!.trigger('click');
    await flushPromises();

    expect(calls).toContain('POST /api/v1/system/setup/complete');
    expect(replace).toHaveBeenCalledWith('/app');
  });

  it('does not call setup/complete before the finish step', async () => {
    const calls = stubBot([step('twitch_app', {
      required: true,
      complete: true,
      fields: [idField],
    })]);

    await mountWizard();

    expect(calls).not.toContain('POST /api/v1/system/setup/complete');
  });
});
