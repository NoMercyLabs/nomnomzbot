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
import { createPinia, setActivePinia, type Pinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, h, ref } from 'vue';

import { i18n } from '@/i18n';

import en from '../i18n/en.json';
import KeysPanel from '../KeysPanel.vue';
import LexiconPanel from '../LexiconPanel.vue';
import RulesPanel from '../RulesPanel.vue';

type Sent = {
  method: string;
  path: string;
  body: unknown;
};

const sent: Sent[] = [];
let lexicon: {
  id: string;
  phrase: string;
  replacement: string;
  matchKind: string;
}[] = [];

const config = {
  isEnabled: true,
  mode: 'all',
  defaultProvider: 'edge',
  defaultVoiceId: null,
  maxCharacters: 300,
  minPermission: 'everyone',
  skipBotMessages: true,
  readUsernames: false,
  profanityCensorEnabled: true,
  modApprovalRequired: false,
  minBitsToTts: 100,
  viewerVoiceSelfServiceEnabled: true,
  hasAzureByokKey: true,
  hasElevenLabsByokKey: false,
  azureRegion: 'westeurope',
  followsPlatformDefaultVoice: true,
};

const reply = (data: unknown): unknown => ({
  data: { data },
  response: { status: 200, ok: true },
});

function write(method: string) {
  return async (path: string, init?: { body?: unknown }): Promise<unknown> => {
    sent.push({
      method,
      path,
      body: init?.body,
    });

    return reply(method === 'PUT' && path.endsWith('/tts/config')
      ? config
      : null);
  };
}

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({ known: ref(true), can: (): boolean => true }),
}));

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string): Promise<unknown> => {
      if (path.endsWith('/tts/config'))
        return reply(config);

      if (path.endsWith('/tts/lexicon'))
        return reply(lexicon);

      return reply({ channelName: 'Stoney_Eagle', pronunciation: 'stoney eagle' });
    },
    PUT: (path: string, init?: { body?: unknown }) => write('PUT')(path, init),
    POST: (path: string, init?: { body?: unknown }) => write('POST')(path, init),
    DELETE: (path: string, init?: { body?: unknown }) => write('DELETE')(path, init),
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;
let pinia: Pinia;

beforeEach(() => {
  sent.length = 0;

  lexicon = [
    {
      id: 'e1',
      phrase: 'gg',
      replacement: 'good game',
      matchKind: 'word',
    },
  ];

  pinia = createPinia();
  setActivePinia(pinia);
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

async function open(): Promise<void> {
  const page = defineComponent({
    render: () => [h(RulesPanel), h(LexiconPanel), h(KeysPanel)],
  });

  mounted = mount(page, {
    attachTo: document.body,
    global: {
      plugins: [
        i18n,
        pinia,
        [
          VueQueryPlugin,
          { queryClient: new QueryClient({ defaultOptions: { queries: { retry: false } } }) },
        ],
      ],
    },
  });

  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();
}

const byLabel = (label: string): HTMLInputElement =>
  document.body.querySelector<HTMLInputElement>(`[aria-label="${label}"]`) as HTMLInputElement;
const button = (label: string): HTMLButtonElement =>
  [...document.body.querySelectorAll<HTMLButtonElement>('button')].find((entry) =>
    entry.textContent?.includes(label),
  ) as HTMLButtonElement;
const switchOf = (title: string): HTMLElement =>
  [...document.body.querySelectorAll<HTMLElement>('[data-slot="setting-row"]')]
    .find((row) => row.textContent?.includes(title))
    ?.querySelector('[role="switch"]') as HTMLElement;

describe('TTS rules panels', () => {
  it('sends the toggled rule to the config and shows the new value at once', async () => {
    await open();
    expect(switchOf('Read usernames').getAttribute('aria-checked')).toBe('false');

    switchOf('Read usernames').click();
    await flushPromises();

    const put = sent.find((call) => call.path.endsWith('/tts/config'));

    expect(put?.method).toBe('PUT');
    expect(put?.body).toEqual({ readUsernames: true });
  });

  it('saves the platform follow switch under the name the bot takes', async () => {
    await open();
    switchOf('Follow the platform voice').click();
    await flushPromises();

    expect(sent.find((call) => call.method === 'PUT')?.body).toEqual({
      followPlatformDefaultVoice: false,
    });
  });

  it('saves the bits gate when the box loses focus, and 0 clears it', async () => {
    await open();

    const box = byLabel('Minimum bits');

    expect(box.value).toBe('100');

    box.value = '0';
    box.dispatchEvent(new Event('input'));
    await flushPromises();
    box.dispatchEvent(new Event('blur'));
    await flushPromises();

    expect(sent.find((call) => call.method === 'PUT')?.body).toEqual({ minBitsToTts: 0 });
  });

  it('adds a lexicon rule with its match kind', async () => {
    await open();

    const [phrase, replacement] = [
      ...document.body.querySelectorAll<HTMLInputElement>('[data-view="tts-lexicon"] form input'),
    ];

    phrase!.value = 'lol';
    phrase!.dispatchEvent(new Event('input'));
    replacement!.value = 'laughing out loud';
    replacement!.dispatchEvent(new Event('input'));
    await flushPromises();
    button('Add rule').click();
    await flushPromises();

    const post = sent.find((call) => call.method === 'POST');

    expect(post?.path).toContain('/tts/lexicon');

    expect(post?.body).toEqual({
      phrase: 'lol',
      replacement: 'laughing out loud',
      matchKind: 'word',
    });
  });

  it('edits and deletes a lexicon rule by its id', async () => {
    await open();
    expect(document.body.querySelectorAll('[data-lexicon-row]')).toHaveLength(1);

    byLabel('Edit the rule for gg').click();
    await flushPromises();
    expect(button('Save rule')).toBeDefined();

    button('Save rule').click();
    await flushPromises();
    expect(sent.find((call) => call.method === 'PUT')?.path).toContain('/tts/lexicon/{entryId}');

    byLabel('Delete the rule for gg').click();
    await flushPromises();
    expect(sent.some((call) => call.method === 'DELETE')).toBe(false);

    (document.body.querySelector('[data-confirm-action]') as HTMLElement).click();
    await flushPromises();
    expect(sent.some((call) => call.method === 'DELETE')).toBe(true);
  });

  it('sends a key once, never shows a stored key and empties the box', async () => {
    await open();

    const box = document.body.querySelector<HTMLInputElement>(
      '[data-key-form="azure"] input[type="password"]',
    ) as HTMLInputElement;

    expect(box.value).toBe('');
    expect(box.placeholder).toBe('A key is stored. Type to replace it.');

    box.value = 'secret-key';
    box.dispatchEvent(new Event('input'));
    await flushPromises();
    button('Save key').click();
    await flushPromises();

    const put = sent.find((call) => call.path.includes('/byok/'));

    expect(put?.body).toEqual({ apiKey: 'secret-key', region: 'westeurope' });
    expect(box.value).toBe('');
    expect(document.body.textContent).not.toContain('secret-key');
  });

  it('saves the channel name spelling', async () => {
    await open();

    const spelling = document.body.querySelector<HTMLInputElement>(
      '[data-view="tts-keys"] form:last-child input',
    ) as HTMLInputElement;

    expect(spelling.value).toBe('stoney eagle');

    spelling.value = 'stoney the eagle';
    spelling.dispatchEvent(new Event('input'));
    await flushPromises();
    button('Save spelling').click();
    await flushPromises();

    const put = sent.find((call) => call.path.includes('name-pronunciation'));

    expect(put?.body).toEqual({ pronunciation: 'stoney the eagle' });
  });
});
