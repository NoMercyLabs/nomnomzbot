// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createMemoryHistory, createRouter, type Router } from 'vue-router';

import { Select } from '@/components/ui/select';
import { i18n } from '@/i18n';

import AccountView from '../AccountView.vue';
import en from '../i18n/en.json';

type Options = { params?: { path?: Record<string, string> }; body?: unknown };
type Answer = {
  data?: unknown;
  error?: unknown;
  response: { status: number; ok: boolean };
};
type Sent = {
  method: string;
  path: string;
  params: Record<string, string>;
  body: unknown;
};

const sent: Sent[] = [];
let answers: Record<string, () => Answer> = {};

const ok = (data: unknown, extra: Record<string, unknown> = {}): Answer => ({
  data: { data, ...extra },
  response: { status: 200, ok: true },
});
const refused = (status: number): Answer => ({
  error: { status: 'error' },
  response: { status, ok: false },
});

function answer(method: string, path: string, options?: Options): Answer {
  sent.push({
    method,
    path,
    params: options?.params?.path ?? {},
    body: options?.body,
  });

  const match = Object.keys(answers).find((key) => {
    const [first, rest] = key.split(' ');

    return rest === undefined
      ? path.endsWith(key)
      : first === method && path.endsWith(rest);
  });

  return match
    ? answers[match]!()
    : refused(404);
}

vi.mock('@/realtime/useViewerMusicLive', () => ({ useViewerMusicLive: () => undefined }));

vi.mock('@/api/client', () => ({
  client: {
    GET: vi.fn<(path: string, options?: Options) => Promise<Answer>>(async (path, options) =>
      answer('GET', path, options)),
    PUT: vi.fn<(path: string, options?: Options) => Promise<Answer>>(async (path, options) =>
      answer('PUT', path, options)),
    DELETE: vi.fn<(path: string, options?: Options) => Promise<Answer>>(async (path, options) =>
      answer('DELETE', path, options)),
  },
}));

const identity = (id: string | undefined, provider: string, isPrimary: boolean) => ({
  ...(id === undefined
    ? {}
    : { id }),
  provider,
  providerUserId: `${provider}-1`,
  providerUsername: `${provider}_user`,
  providerDisplayName: `${provider} name`,
  isPrimary,
  linkedAt: '2026-03-04T10:00:00Z',
});

const voice = (id: string, displayName: string) => ({
  id,
  name: id,
  displayName,
  locale: 'en-US',
  gender: 'female',
  provider: 'azure',
  isDefault: false,
  styles: [],
  tags: [],
});

let myPronoun = {
  pronounId: 1,
  altPronounId: null as number | null,
  manualOverride: true,
};
let identities = [identity('i-1', 'twitch', true), identity('i-2', 'kick', false)];
let myVoice: { userId: string; voiceId: string } | null = null;

function freshAnswers(): Record<string, () => Answer> {
  return {
    '/public/sr/by-channel/{channelName}': () =>
      ok({
        broadcasterId: 'chan-1',
        channelName: 'Stoney_Eagle',
        enabledProviders: [],
      }),
    'GET /pronouns/catalog': () =>
      ok([
        {
          id: 1,
          name: 'he/him',
          subject: 'he',
          object: 'him',
        },
        {
          id: 2,
          name: 'she/her',
          subject: 'she',
          object: 'her',
        },
      ]),
    'GET /pronouns/me': () => ok(myPronoun),
    'PUT /pronouns/me': () => {
      const last = sent.at(-1)!.body as { pronounId?: number };

      myPronoun = { ...myPronoun, pronounId: last.pronounId ?? myPronoun.pronounId };

      return ok(myPronoun);
    },
    'GET /auth/identities': () => ok(identities),
    'DELETE /auth/identities/{identityId}': () => {
      const id = sent.at(-1)!.params.identityId;

      identities = identities.filter((row) => (row as { id?: string }).id !== id);

      return ok({});
    },
    '/community/me/standing': () => ok({ standing: 'Subscriber', levelValue: 1 }),
    'GET /tts/me/voice': () => ok(myVoice),
    'PUT /tts/me/voice': () => {
      myVoice = { userId: 'u-1', voiceId: (sent.at(-1)!.body as { voiceId: string }).voiceId };

      return ok(myVoice);
    },
    'DELETE /tts/me/voice': () => {
      myVoice = null;

      return ok({});
    },
    'GET /tts/voices': () =>
      ok([voice('v-1', 'Aria'), voice('v-2', 'Guy')], { hasMore: false, total: 2 }),
  };
}

async function mountPage(): Promise<VueWrapper> {
  i18n.global.mergeLocaleMessage('en', en);
  const router: Router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/me/:channel/me', component: AccountView },
      {
        path: '/app/settings/your-data',
        name: 'dashboard-settings-your-data',
        component: AccountView,
      },
    ],
  });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  await router.push('/me/stoney_eagle/me');
  const wrapper: VueWrapper = mount(AccountView, {
    attachTo: document.body,
    global: { plugins: [router, createPinia(), [VueQueryPlugin, { queryClient }], i18n] },
  });

  await flushPromises();

  return wrapper;
}

beforeEach(() => {
  sent.length = 0;
  answers = freshAnswers();

  myPronoun = {
    pronounId: 1,
    altPronounId: null,
    manualOverride: true,
  };

  identities = [identity('i-1', 'twitch', true), identity('i-2', 'kick', false)];
  myVoice = null;
});

afterEach(() => {
  document.body.innerHTML = '';
  vi.clearAllMocks();
});

describe('the viewer account page', () => {
  it('sends the picked pronoun in the PUT body and shows the saved pronoun after', async () => {
    const wrapper = await mountPage();
    const primary = wrapper.get('[data-viewer-pronouns]').findAllComponents(Select)[0]!;

    expect(primary.props('modelValue')).toBe('1');

    primary.vm.$emit('update:modelValue', '2');
    await flushPromises();

    const put = sent.find((call) => call.method === 'PUT' && call.path.endsWith('/pronouns/me'));

    expect(put?.body).toEqual({ pronounId: 2 });
    expect(wrapper.get('[data-viewer-pronouns]').findAllComponents(Select)[0]!.props('modelValue')).toBe('2');
  });

  it('asks before an unlink, then removes the row once the bot accepts', async () => {
    const wrapper = await mountPage();

    expect(wrapper.findAll('[data-viewer-account-row]')).toHaveLength(2);

    await wrapper.get('[data-unlink]').trigger('click');
    await flushPromises();

    expect(sent.some((call) => call.method === 'DELETE')).toBe(false);
    expect(document.body.textContent).toContain('Unlink kick name?');

    const confirm = Array.from(document.body.querySelectorAll('button')).find(
      (button) => button.textContent?.trim() === 'Unlink' && button.closest('[role="alertdialog"]'),
    );

    confirm!.click();
    await flushPromises();

    expect(sent.find((call) => call.method === 'DELETE')?.params.identityId).toBe('i-2');
    expect(wrapper.findAll('[data-viewer-account-row]')).toHaveLength(1);
    expect(wrapper.get('[data-viewer-accounts]').text()).not.toContain('kick name');
  });

  it('offers no unlink on the main sign-in or on a row the bot sends without an id', async () => {
    identities = [identity('i-1', 'twitch', true), identity(undefined, 'kick', false)];
    const wrapper = await mountPage();

    expect(wrapper.findAll('[data-viewer-account-row]')).toHaveLength(2);
    expect(wrapper.find('[data-unlink]').exists()).toBe(false);
  });

  it('shows the standing by name and never the level number', async () => {
    const wrapper = await mountPage();
    const text = wrapper.get('[data-viewer-standing]').text();

    expect(text).toContain('Subscriber');
    expect(text).not.toMatch(/\d/);
  });

  it('puts the picked voice in the own-voice PUT, then shows it as the voice in use', async () => {
    const wrapper = await mountPage();

    expect(wrapper.get('[data-channel-voice]').text()).toContain('channel voice');

    await wrapper.findAll('[data-voice-use]')[1]!.trigger('click');
    await flushPromises();

    const put = sent.find((call) => call.method === 'PUT' && call.path.endsWith('/tts/me/voice'));

    expect(put?.body).toEqual({ voiceId: 'v-2' });
    expect(put?.params.channelId).toBe('chan-1');
    expect(sent.some((call) => call.path.endsWith('/tts/config'))).toBe(false);
    expect(sent.filter((call) => call.method !== 'GET').map((call) => call.path)).toEqual([put?.path]);
    expect(wrapper.findAll('[data-voice-row]')[1]!.text()).toContain('Your voice');
  });

  it('goes back to the channel voice when the person resets their own', async () => {
    myVoice = { userId: 'u-1', voiceId: 'v-1' };
    const wrapper = await mountPage();

    await wrapper.get('[data-voice-reset]').trigger('click');
    await flushPromises();

    expect(sent.some((call) => call.method === 'DELETE' && call.path.endsWith('/tts/me/voice'))).toBe(true);
    expect(wrapper.find('[data-voice-reset]').exists()).toBe(false);
  });

  it('says why the voices are missing when the bot refuses the catalog', async () => {
    answers['GET /tts/voices'] = () => refused(403);
    const wrapper = await mountPage();

    expect(wrapper.get('[data-viewer-voice]').text()).toContain('has not turned on voice choice');
  });
});
