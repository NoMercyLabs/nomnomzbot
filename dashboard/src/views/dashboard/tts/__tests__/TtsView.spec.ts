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
import { ref } from 'vue';
import { createMemoryHistory, createRouter, type Router } from 'vue-router';

import { laptopWidth, setWidth } from '@/__tests__/viewport';
import { i18n } from '@/i18n';

import en from '../i18n/en.json';
import TtsView from '../TtsView.vue';

type Answer = {
  data?: unknown;
  error?: unknown;
  response: { status: number; ok: boolean };
};

const calls: string[] = [];
let answers: Record<string, () => Answer> = {};

const ok = (data: unknown): Answer => ({ data: { data }, response: { status: 200, ok: true } });
const refused = (): Answer => ({
  error: { status: 'error' },
  response: { status: 403, ok: false },
});

const config = {
  isEnabled: true,
  mode: 'all',
  defaultProvider: 'edge',
  defaultVoiceId: 'en-US-AriaNeural',
  maxCharacters: 300,
  minPermission: 'everyone',
  skipBotMessages: true,
  readUsernames: false,
  profanityCensorEnabled: true,
  modApprovalRequired: false,
  minBitsToTts: null,
  viewerVoiceSelfServiceEnabled: true,
  hasAzureByokKey: false,
  hasElevenLabsByokKey: false,
  azureRegion: null,
  followsPlatformDefaultVoice: false,
};

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({ known: ref(true), can: (): boolean => true }),
}));

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string) => {
      calls.push(path);
      const match = Object.keys(answers).find((suffix) => path.endsWith(suffix));

      return match
        ? answers[match]!()
        : ok(null);
    },
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;
let pinia: Pinia;

beforeEach(() => {
  setWidth(laptopWidth);
  calls.length = 0;

  answers = {
    '/tts/config': () => ok(config),
  };

  pinia = createPinia();
  setActivePinia(pinia);
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

async function open(query: string = ''): Promise<Router> {
  const blank = { template: '<div />' };
  const router: Router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/app/tts',
        name: 'dashboard-tts',
        component: blank,
      },
      {
        path: '/app/obs',
        name: 'dashboard-obs',
        component: blank,
      },
    ],
  });

  await router.push(`/app/tts${query}`);

  mounted = mount(TtsView, {
    attachTo: document.body,
    global: {
      plugins: [
        i18n,
        pinia,
        router,
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

  return router;
}

const text = (): string => document.body.textContent ?? '';
const panel = (title: string): HTMLElement =>
  [...document.body.querySelectorAll<HTMLElement>('[data-slot="panel-card"]')].find((card) =>
    card.querySelector('h2')?.textContent?.includes(title),
  ) as HTMLElement;
const rowOf = (title: string): HTMLElement =>
  [...panel('Settings').querySelectorAll<HTMLElement>('[data-settings-row]')].find((row) =>
    row.textContent?.includes(title),
  ) as HTMLElement;
const badgeTone = (): string | null =>
  document.body.querySelector('[data-slot="status-badge"]')?.getAttribute('data-tone') ?? null;

describe('TtsView overview', () => {
  it('shows the five tabs and starts on Overview', async () => {
    await open();

    const tabs: string[] = [...document.body.querySelectorAll('[role="tab"]')].map(
      (tab) => tab.textContent?.trim() ?? '',
    );

    expect(tabs).toEqual(['Overview', 'Voices', 'Rules', 'Pronunciation', 'Keys']);
    expect(panel('Spoken history')).toBeDefined();
  });

  it('shows an on badge when text to speech is enabled and an off badge when it is not', async () => {
    await open();
    expect(badgeTone()).toBe('ok');
    expect(text()).toContain('Text to speech is on');

    mounted?.unmount();
    document.body.innerHTML = '';
    answers['/tts/config'] = () => ok({ ...config, isEnabled: false });
    await open();
    expect(badgeTone()).toBe('off');
    expect(text()).toContain('Text to speech is off');
  });

  it('names the channel voice like the Voices tab does, not by its raw id', async () => {
    answers['/tts/voices'] = () =>
      ok([
        {
          id: 'en-US-AriaNeural',
          displayName: 'Aria (US)',
          locale: 'en-US',
          gender: 'female',
          provider: 'edge',
        },
      ]);

    await open();
    await flushPromises();

    expect(rowOf('Voice').textContent).toContain('Aria (US)');
    expect(rowOf('Voice').textContent).not.toContain('en-US-AriaNeural');
  });

  it('builds the settings rows from the config: voice, who may use it, limits, words', async () => {
    await open();

    expect(rowOf('Voice').textContent).toContain('en-US-AriaNeural');
    expect(rowOf('Voice').textContent).toContain('Microsoft Edge');
    expect(rowOf('Who may use it').textContent).toContain('everyone');
    expect(rowOf('Message limits').textContent).toContain('300 characters');
    expect(rowOf('Message limits').textContent).toContain('Bot messages are skipped');
    expect(rowOf('Words and pronunciation').textContent).toContain('Profanity is censored');
  });

  it('ends every settings row with a chevron, because each one opens something', async () => {
    await open();

    const rowsOpen: HTMLElement[] = [
      ...panel('Settings').querySelectorAll<HTMLElement>('[data-settings-row]'),
    ];

    expect(rowsOpen).toHaveLength(5);

    for (const row of rowsOpen) {
      expect(row.querySelector('[data-slot="summary-row-chevron"]')).not.toBeNull();
    }
  });

  it('says the channel follows the platform voice when no voice is picked', async () => {
    answers['/tts/config'] = () =>
      ok({
        ...config,
        defaultVoiceId: null,
        followsPlatformDefaultVoice: true,
      });

    await open();

    expect(rowOf('Voice').textContent).toContain('Follows the platform voice');
  });

  it('sends the speaker overlay row to the Required overlays panel on the OBS page', async () => {
    const router = await open();

    expect(rowOf('Speaker overlay').textContent).toContain('Required overlays on the OBS page');
    expect(calls.some((path) => path.endsWith('/tts/overlay'))).toBe(false);

    rowOf('Speaker overlay').click();
    await flushPromises();

    expect(router.currentRoute.value.name).toBe('dashboard-obs');
    expect(router.currentRoute.value.hash).toBe('#required-overlays');
  });

  it('gives the empty spoken history its reason instead of a made-up list', async () => {
    await open();

    expect(panel('Spoken history').getAttribute('data-state')).toBe('empty');

    expect(panel('Spoken history').textContent).toContain(
      'The bot does not keep a readable record of spoken messages yet.',
    );
  });

  it('shows the error state when the config read is refused, and no badge', async () => {
    answers['/tts/config'] = () => refused();
    await open();

    expect(panel('Settings').getAttribute('data-state')).toBe('error');
    expect(badgeTone()).toBeNull();
  });

  it('opens Rules with the speaking and rules panels and neither lexicon nor keys', async () => {
    await open('?view=rules');
    expect(panel('Message rules').getAttribute('data-state')).toBe('ready');
    expect(document.body.querySelector('[data-view="tts-speaking"]')).not.toBeNull();
    expect(document.body.querySelector('[data-view="tts-lexicon"]')).toBeNull();
    expect(document.body.querySelector('[data-view="tts-keys"]')).toBeNull();
  });

  it('opens Keys on its own tab with the keys panel alone', async () => {
    await open('?view=keys');
    expect(panel('Keys and name')).toBeDefined();
    expect(document.body.querySelector('[data-view="tts-rules"]')).toBeNull();
  });

  it('opens Pronunciation on its own tab with the lexicon panel alone', async () => {
    await open('?view=pronunciation');
    expect(panel('Pronunciation')).toBeDefined();
    expect(document.body.querySelector('[data-view="tts-rules"]')).toBeNull();
  });
});

// A-tts defects 8, 9 and 10 (2026-10-19).
describe('TtsView polish', () => {
  // Spoken history has a description, so its title sits at the top of a taller head; the Settings panel beside it
  // needs a description too, or its title is centered in a shorter head and sits lower.
  it('gives both overview panels a description so their titles share a line', async () => {
    await open();

    expect(panel('Spoken history').querySelector('header p')?.textContent).toBe(
      en.dashboard.tts.history.description,
    );

    expect(panel('Settings').querySelector('header p')?.textContent).toBe(
      en.dashboard.tts.settings.description,
    );
  });

  it('shows an example in the empty Azure region box', async () => {
    await open('?view=keys');

    const region = document.body.querySelector<HTMLInputElement>(
      'input[aria-label="Azure region"]',
    );

    expect(region?.value).toBe('');
    expect(region?.placeholder).toBe(en.dashboard.tts.keys.regionPlaceholder);
  });

  it('offers the same reset in the Rules panel as in the Speaking panel', async () => {
    await open('?view=rules');

    const labels = ['tts-speaking', 'tts-rules'].map((view) =>
      document.body
        .querySelector(`[data-view="${view}"] footer [data-tts-reset]`)
        ?.textContent?.trim(),
    );

    expect(labels).toEqual(['Reset to defaults', 'Reset to defaults']);
  });
});
