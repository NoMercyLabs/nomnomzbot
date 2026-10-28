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

import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia, type Pinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ref } from 'vue';
import { createMemoryHistory, createRouter, type Router } from 'vue-router';

import { laptopWidth, phoneWidth, setWidth } from '@/__tests__/viewport';
import { i18n } from '@/i18n';

import en from '../i18n/en.json';
import TtsView from '../TtsView.vue';

type Answer = {
  data?: unknown;
  error?: unknown;
  response: { status: number; ok: boolean };
};

const calls: string[] = [];
const queries: unknown[] = [];
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
    GET: async (path: string, options?: { params?: { query?: unknown } }) => {
      calls.push(path);
      queries.push(options?.params?.query);
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
  queries.length = 0;

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
    expect(router.currentRoute.value.query.view).toBe('overlays');
  });

  it('reads the spoken history from the server and says so when it is empty', async () => {
    answers['/tts/history'] = () => ok({ items: [], nextBefore: null });

    await open();

    expect(calls.some((path) => path.endsWith('/tts/history'))).toBe(true);
    expect(panel('Spoken history').getAttribute('data-state')).toBe('empty');

    expect(panel('Spoken history').textContent).toContain('Nothing has been read aloud yet.');
  });

  it('lists what was spoken with the text, the viewer, the voice name and the time', async () => {
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

    answers['/tts/history'] = () =>
      ok({
        items: [
          {
            id: 7,
            text: 'hello chat',
            voiceId: 'en-US-AriaNeural',
            provider: 'edge',
            requestedByUserId: '42',
            requestedByName: 'Viewer One',
            requestedByColor: '#ff00ff',
            occurredAt: '2026-10-20T10:00:00Z',
          },
          {
            id: 6,
            text: null,
            voiceId: 'en-US-AriaNeural',
            provider: 'edge',
            requestedByUserId: '43',
            requestedByName: null,
            requestedByColor: null,
            occurredAt: '2026-10-19T10:00:00Z',
          },
        ],
        nextBefore: null,
      });

    await open();

    const rows = [...panel('Spoken history').querySelectorAll<HTMLElement>('[data-history-row]')];

    expect(rows).toHaveLength(2);
    expect(rows[0]!.textContent).toContain('hello chat');
    expect(rows[0]!.textContent).toContain('Viewer One');
    expect(rows[0]!.textContent).toContain('Aria (US)');
    expect(rows[0]!.querySelector('time')).not.toBeNull();
    expect(rows[1]!.textContent).toContain('Spoken before the bot kept the text.');
    expect(rows[1]!.textContent).not.toContain('Unknown viewer');
    expect(rows[1]!.querySelector('[data-slot="user-name"], button, a')).toBeNull();
    expect(panel('Spoken history').querySelector('[data-slot="history-older"]')).toBeNull();
  });

  it('looks a spoken voice up by its id when it is not in the first catalog page', async () => {
    answers['/tts/voices'] = () => {
      const asked = (queries[queries.length - 1] as { q?: string } | undefined)?.q;

      return ok(
        asked === 'ru-RU-SvetlanaNeural'
          ? [
              {
                id: 'ru-RU-SvetlanaNeural',
                displayName: 'Svetlana (Russia)',
                locale: 'ru-RU',
                gender: 'female',
                provider: 'edge',
              },
            ]
          : [],
      );
    };

    answers['/tts/history'] = () =>
      ok({
        items: [
          {
            id: 5,
            text: 'privet',
            voiceId: 'ru-RU-SvetlanaNeural',
            provider: 'edge',
            requestedByUserId: '42',
            requestedByName: 'Viewer One',
            requestedByColor: null,
            occurredAt: '2026-10-20T10:00:00Z',
          },
        ],
        nextBefore: null,
      });

    await open();
    await flushPromises();

    const row = panel('Spoken history').querySelector<HTMLElement>('[data-history-row]')!;

    expect(row.textContent).toContain('Svetlana (Russia)');
    expect(row.textContent).not.toContain('ru-RU-SvetlanaNeural');
  });

  it('asks for the next older page with the cursor when Show older messages is pressed', async () => {
    const entry = (id: number): Record<string, unknown> => ({
      id,
      text: `line ${id}`,
      voiceId: 'v',
      provider: 'edge',
      requestedByUserId: '42',
      requestedByName: 'Viewer One',
      requestedByColor: null,
      occurredAt: '2026-10-20T10:00:00Z',
    });

    let reads = 0;

    answers['/tts/history'] = () => {
      reads += 1;

      return reads === 1
        ? ok({ items: [entry(2)], nextBefore: 2 })
        : ok({ items: [entry(1)], nextBefore: null });
    };

    await open();

    const older = panel('Spoken history').querySelector<HTMLElement>('[data-slot="history-older"]');

    expect(older).not.toBeNull();

    older!.click();
    await flushPromises();

    const historyQueries = calls
      .map((path, index) =>
        path.endsWith('/tts/history')
          ? queries[index]
          : undefined)
      .filter((query) => query !== undefined);

    expect(historyQueries).toEqual([{ limit: 50 }, { limit: 50, before: 2 }]);
    expect(panel('Spoken history').textContent).toContain('line 2');
    expect(panel('Spoken history').textContent).toContain('line 1');
    expect(panel('Spoken history').querySelector('[data-slot="history-older"]')).toBeNull();
  });

  it('shows the error state, not an empty list, when the spoken history read is refused', async () => {
    answers['/tts/history'] = () => refused();

    await open();

    expect(panel('Spoken history').getAttribute('data-state')).toBe('error');
    expect(panel('Spoken history').textContent).toContain('could not be loaded');
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

  // T6c defect 3: the content jumped in width between tabs (Pronunciation 8 of 12, Keys 6 of 12, Rules 12 of 12).
  it.each(['pronunciation', 'keys', 'rules', 'voices', 'overview'])(
    'fills the same 12 columns on the %s tab',
    async (view) => {
      await open(`?view=${view}`);

      const spans = [...document.body.querySelectorAll<HTMLElement>('[data-slot="board-cell"]')]
        .map((cell) => cell.style.getPropertyValue('--cell-span'));
      const total: number = spans.reduce((sum, span) => sum + Number(span.replace('span ', '')), 0);

      expect(total % 12).toBe(0);
    },
  );
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

// Stoney 2026-10-21: "Mobile needs more scrolling pages that are longer". A phone shows every panel of a tab
// stacked in the one page scroll, so the page has one tab band and no second panel picker.
describe('TtsView on a phone', () => {
  const entry = (id: number): Record<string, unknown> => ({
    id,
    text: `line ${id}`,
    voiceId: 'v',
    provider: 'edge',
    requestedByUserId: '42',
    requestedByName: 'Viewer One',
    requestedByColor: null,
    occurredAt: '2026-10-20T10:00:00Z',
  });

  beforeEach(() => {
    setWidth(phoneWidth);
    answers['/tts/history'] = () => ok({ items: [entry(2), entry(1)], nextBefore: 1 });
  });

  it('stacks the settings and the history in one page with no second tab strip', async () => {
    await open();

    expect(document.body.querySelector('[data-slot="board-panel-picker"]')).toBeNull();
    expect(document.body.querySelector('[data-slot="board-grid"]')).toBeNull();

    const stack = document.body.querySelector('[data-slot="tts-stack"]');
    const titles = [...(stack?.querySelectorAll('h2') ?? [])].map((title) => title.textContent);

    expect(titles).toEqual(['Settings', 'Spoken history']);
  });

  it('lists the spoken history at its natural height with Show older messages at its end', async () => {
    await open();

    const history = panel('Spoken history');
    const rows = history.querySelectorAll('[data-history-row]');
    const older = history.querySelector<HTMLElement>('[data-slot="history-older"]');

    expect(rows).toHaveLength(2);
    expect(history.closest('[data-slot="tts-stack"]')).not.toBeNull();
    expect(older?.textContent).toContain('Show older messages');
    expect(older?.closest('footer')).not.toBeNull();
  });

  it('stacks the three panels of Rules and the one of Keys without a picker', async () => {
    await open('?view=rules');
    expect(document.body.querySelector('[data-slot="board-panel-picker"]')).toBeNull();

    expect(
      document.body.querySelectorAll('[data-slot="tts-stack"] [data-slot="panel-card"]'),
    ).toHaveLength(3);

    mounted?.unmount();
    mounted = null;
    await open('?view=keys');
    expect(document.body.querySelector('[data-slot="board-panel-picker"]')).toBeNull();

    expect(
      document.body.querySelectorAll('[data-slot="tts-stack"] [data-slot="panel-card"]'),
    ).toHaveLength(1);
  });

  it('keeps the board at laptop width', async () => {
    setWidth(laptopWidth);
    await open();

    expect(document.body.querySelector('[data-slot="tts-stack"]')).toBeNull();
    expect(document.body.querySelector('[data-slot="board-grid"]')).not.toBeNull();
  });
});
