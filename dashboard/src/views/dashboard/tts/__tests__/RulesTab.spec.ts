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
import { defineComponent, h, ref } from 'vue';

import { i18n } from '@/i18n';

import en from '../i18n/en.json';
import KeysPanel from '../KeysPanel.vue';
import LexiconPanel from '../LexiconPanel.vue';
import RulesPanel from '../RulesPanel.vue';
import SpeakingPanel from '../SpeakingPanel.vue';
import TtsRewardsPanel from '../TtsRewardsPanel.vue';

type Sent = {
  method: string;
  path: string;
  body: unknown;
};

const sent: Sent[] = [];
let configRefused = false;
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
  defaultVoiceId: null as string | null,
  maxCharacters: 300,
  minPermission: 'everyone',
  skipBotMessages: true,
  readUsernames: false,
  profanityCensorEnabled: true,
  modApprovalRequired: false,
  minBitsToTts: 100,
  cooldownSeconds: 30,
  viewerVoiceSelfServiceEnabled: true,
  hasAzureByokKey: true,
  hasElevenLabsByokKey: false,
  azureRegion: 'westeurope',
  followsPlatformDefaultVoice: true,
};

const defaults = {
  ...config,
  maxCharacters: 500,
  minPermission: 'everyone',
  mode: 'self_host',
  readUsernames: true,
  minBitsToTts: null,
  cooldownSeconds: 0,
};

const options = {
  providers: ['edge', 'azure'],
  modes: ['self_host', 'byok'],
  minPermissions: ['everyone', 'moderators'],
  maxCharactersLimit: 400,
  cooldownSecondsDefault: 0,
  cooldownSecondsMax: 3600,
};

let rewards: {
  id: string;
  title: string;
  cost: number;
  speaksViaTts: boolean;
}[] = [];
const refusals = {
  cooldownSeconds: 'tts.config.error.cooldownOutOfRange',
  maxCharacters: 'tts.config.error.maxCharactersOutOfRange',
  minBitsToTts: 'tts.config.error.minBitsOutOfRange',
} as const;
let refusedField: keyof typeof refusals | null = null;

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

    if (refusedField !== null && method === 'PUT' && path.endsWith('/tts/config')) {
      return {
        data: undefined,
        error: { data: { key: refusals[refusedField], field: refusedField } },
        response: { status: 400, ok: false },
      };
    }

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
      if (path.endsWith('/tts/config') && configRefused) {
        return {
          data: undefined,
          error: undefined,
          response: { status: 429, ok: false },
        };
      }

      if (path.endsWith('/tts/config'))
        return reply(config);

      if (path.endsWith('/tts/config/options'))
        return reply(options);

      if (path.endsWith('/tts/config/defaults'))
        return reply(defaults);

      if (path.endsWith('/tts/lexicon'))
        return reply(lexicon);

      if (path.endsWith('/rewards'))
        return reply(rewards);

      return reply({ channelName: 'Stoney_Eagle', pronunciation: 'stoney eagle' });
    },
    PUT: (path: string, init?: { body?: unknown }) => write('PUT')(path, init),
    POST: (path: string, init?: { body?: unknown }) => write('POST')(path, init),
    DELETE: (path: string, init?: { body?: unknown }) => write('DELETE')(path, init),
    PATCH: (path: string, init?: { body?: unknown }) => write('PATCH')(path, init),
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;
let pinia: Pinia;

beforeEach(() => {
  sent.length = 0;
  configRefused = false;
  refusedField = null;

  rewards = [
    {
      id: 'r1',
      title: 'Say it out loud',
      cost: 500,
      speaksViaTts: false,
    },
  ];

  config.mode = 'all';
  config.defaultProvider = 'edge';
  options.providers = ['edge', 'azure'];
  defaults.azureRegion = 'westeurope';

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
    render: () => [
      h(RulesPanel),
      h(SpeakingPanel),
      h(TtsRewardsPanel),
      h(LexiconPanel),
      h(KeysPanel),
    ],
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
  it('shows an error with a retry, not a blank panel, when the config read is refused with 429', async () => {
    configRefused = true;
    await open();

    const panel = document.body.querySelector('[data-view="tts-rules"]') as HTMLElement;
    const failure = panel.querySelector('[data-slot="panel-error"]');

    expect(failure).not.toBeNull();
    expect(failure?.textContent).toContain('Try again');

    configRefused = false;
    const retry = failure?.querySelector('button') as HTMLButtonElement;

    retry.click();
    await flushPromises();
    await new Promise((done) => setTimeout(done, 0));
    await flushPromises();

    expect(panel.querySelector('[data-slot="panel-error"]')).toBeNull();
    expect(switchOf('Read usernames')).toBeTruthy();
  });

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

  it('saves the speaking switches and names each default', async () => {
    await open();
    expect(switchOf('Skip bot messages').getAttribute('aria-checked')).toBe('true');
    expect(document.body.textContent).toContain('Default: on');

    switchOf('Skip bot messages').click();
    await flushPromises();
    switchOf('Censor profanity').click();
    await flushPromises();
    switchOf('Read messages aloud').click();
    await flushPromises();

    const bodies = sent.filter((call) => call.method === 'PUT').map((call) => call.body);

    expect(bodies).toEqual([
      { skipBotMessages: false },
      { profanityCensorEnabled: false },
      { isEnabled: false },
    ]);
  });

  // The Speaking panel scrolled at 1440x699 with 7 rows; the profanity filter is "what the bot reads aloud",
  // which is the Message rules panel, so each panel holds six rows and fits the board without a scroller.
  it('keeps the profanity filter in the Message rules panel, not Speaking', async () => {
    await open();

    const rowsIn = (view: string): string[] =>
      [...document.body.querySelectorAll(`[data-view="${view}"] [data-slot="setting-row"]`)].map(
        (row) => row.textContent ?? '',
      );

    expect(rowsIn('tts-rules').join('|')).toContain('Censor profanity');
    expect(rowsIn('tts-speaking').join('|')).not.toContain('Censor profanity');
    expect(rowsIn('tts-speaking').join('|')).toContain('Skip bot messages');
  });

  it('saves the character cap on blur and shows the default from the bot', async () => {
    await open();

    const box = byLabel('Maximum characters');

    expect(box.value).toBe('300');
    expect(document.body.textContent).toContain('Default: 500 characters');

    box.value = '250';
    box.dispatchEvent(new Event('input'));
    await flushPromises();
    box.dispatchEvent(new Event('blur'));
    await flushPromises();

    expect(sent.find((call) => call.method === 'PUT')?.body).toEqual({ maxCharacters: 250 });
  });

  it('saves the voice source and the lowest standing', async () => {
    await open();

    const segment = [
      ...document.body.querySelectorAll<HTMLButtonElement>('[data-segmented] button'),
    ].find((entry) => entry.textContent?.trim() === 'My own key');

    segment?.click();
    await flushPromises();
    expect(sent.find((call) => call.method === 'PUT')?.body).toEqual({ mode: 'byok' });

    const standing = byLabel('Who may be heard');
    const moderators = [...standing.querySelectorAll<HTMLButtonElement>('button')].find(
      (entry) => entry.textContent?.trim() === 'Moderators',
    );

    moderators?.click();
    await flushPromises();

    expect(sent.filter((call) => call.method === 'PUT')[1]?.body).toEqual({
      minPermission: 'moderators',
    });
  });

  it('saves the cooldown on blur, keeps it inside the range and names the default', async () => {
    await open();

    const box = byLabel('Cooldown');

    expect(box.value).toBe('30');
    expect(box.max).toBe('3600');
    expect(document.body.textContent).toContain('Default: 0 seconds');

    box.value = '9999';
    box.dispatchEvent(new Event('input'));
    await flushPromises();
    box.dispatchEvent(new Event('blur'));
    await flushPromises();

    expect(sent.find((call) => call.method === 'PUT')?.body).toEqual({ cooldownSeconds: 3600 });
  });

  it.each([
    ['cooldownSeconds', 'Cooldown', '12', 'The cooldown is between 0 and 3600 seconds.'],
    ['maxCharacters', 'Maximum characters', '120', 'The character limit is between 1 and 500.'],
    ['minBitsToTts', 'Minimum bits', '50', 'The minimum bits is between 0 and 1,000,000.'],
  ] as const)('shows the translated line for a refused %s next to its box only', async (field, label, value, line) => {
    refusedField = field;
    await open();

    const box = byLabel(label);

    box.value = value;
    box.dispatchEvent(new Event('input'));
    await flushPromises();
    box.dispatchEvent(new Event('blur'));
    await flushPromises();

    const lines = [...document.body.querySelectorAll('[data-problem]')];

    expect(lines.map((entry) => entry.getAttribute('data-problem'))).toEqual([field]);
    expect(lines[0]?.textContent?.trim()).toBe(line);
  });

  it('switches a reward to speak its viewer text through TTS and sends only that field', async () => {
    await open();

    const panel = document.body.querySelector('[data-view="tts-rewards"]') as HTMLElement;
    const row = panel.querySelector('[data-slot="setting-row"]') as HTMLElement;
    const toggle = row.querySelector('[role="switch"]') as HTMLElement;

    expect(row.textContent).toContain('Say it out loud');
    expect(row.textContent).toContain('500 points');
    expect(toggle.getAttribute('aria-checked')).toBe('false');

    toggle.click();
    await flushPromises();

    const patch = sent.find((call) => call.method === 'PATCH');

    expect(patch?.path).toContain('/rewards/{rewardId}');
    expect(patch?.body).toEqual({ speaksViaTts: true });
  });

  it('says so when the channel has no rewards to switch', async () => {
    rewards = [];
    await open();

    const panel = document.body.querySelector('[data-view="tts-rewards"]') as HTMLElement;

    expect(panel.querySelector('[role="switch"]')).toBeNull();
    expect(panel.textContent).toContain('No rewards yet');
  });

  it('offers only the providers, standings and cap the bot serves', async () => {
    await open();

    const segments = [
      ...document.body.querySelectorAll<HTMLButtonElement>('[data-segmented] button'),
    ].map((entry) => entry.textContent?.trim());

    expect(segments).toContain('Microsoft Edge');
    expect(segments).not.toContain('ElevenLabs');

    const box = byLabel('Maximum characters');

    expect(box.max).toBe('400');

    box.value = '999';
    box.dispatchEvent(new Event('input'));
    await flushPromises();
    box.dispatchEvent(new Event('blur'));
    await flushPromises();

    expect(sent.find((call) => call.method === 'PUT')?.body).toEqual({ maxCharacters: 400 });
  });

  // T6c defect 7: "Voice provider" offered Azure and ElevenLabs while the voice source was the free bot voice.
  const providerSegments = (): (string | undefined)[] =>
    [...document.body.querySelectorAll<HTMLButtonElement>('[data-segmented] button')].map((entry) =>
      entry.textContent?.trim(),
    );

  it('offers only the free provider while the voice source is the free bot voice', async () => {
    config.mode = 'self_host';
    options.providers = ['edge', 'azure', 'elevenlabs'];
    await open();

    expect(providerSegments()).toContain('Microsoft Edge');
    expect(providerSegments()).not.toContain('Azure');
    expect(providerSegments()).not.toContain('ElevenLabs');
  });

  it('offers only the keyed providers while the voice source is my own key', async () => {
    config.mode = 'byok';
    config.defaultProvider = 'azure';
    options.providers = ['edge', 'azure', 'elevenlabs'];
    await open();

    expect(providerSegments()).not.toContain('Microsoft Edge');
    expect(providerSegments()).toContain('Azure');
    expect(providerSegments()).toContain('ElevenLabs');
  });

  it('names the default under the Azure region box like the rule rows do', async () => {
    await open();

    expect(document.body.querySelector('[data-region-default]')?.textContent?.trim()).toBe(
      'Default: westeurope',
    );
  });

  // A-tts defect 5: the caption said "Default: none" while the bot falls back to westeurope. No default from the endpoint
  // means no caption, never a false one.
  it('shows no default caption under the Azure region box when the bot returns none', async () => {
    defaults.azureRegion = null as unknown as string;
    await open();

    expect(document.body.querySelector('[data-region-default]')).toBeNull();
  });

  it('resets the rules to the bot defaults through the reset call', async () => {
    await open();
    button('Reset to defaults').click();
    await flushPromises();

    const post = sent.find((call) => call.path.endsWith('/tts/config/reset'));

    expect(post?.method).toBe('POST');
  });

  // The column labels are drawn once, in one header row; a rule row holds only values.
  it('names the columns once in a header row and keeps the labels out of the rule rows', async () => {
    await open();

    const headers = document.body.querySelectorAll('[data-view="tts-lexicon"] [data-lexicon-header]');
    const row = document.body.querySelector('[data-lexicon-row]') as HTMLElement;

    expect(headers).toHaveLength(1);
    expect(headers[0]!.textContent).toContain('Phrase');
    expect(headers[0]!.textContent).toContain('Say instead');
    expect(headers[0]!.textContent).toContain('Match');
    expect(row.textContent).not.toContain('Phrase');
    expect(row.textContent).not.toContain('Say instead');
    expect(row.textContent).not.toContain('Match');
  });

  // The header and every rule row are four cells wide on one shared grid, so the columns line up.
  it('lays the header and the rule rows out on one four-column grid', async () => {
    await open();

    const header = document.body.querySelector('[data-lexicon-header]') as HTMLElement;
    const row = document.body.querySelector('[data-lexicon-row]') as HTMLElement;
    const columns = (element: HTMLElement): string | undefined =>
      element.className.split(/\s+/).find((token) => token.startsWith('md:grid-cols-'));

    expect(columns(header)).toBeDefined();
    expect(columns(row)).toBe(columns(header));
    expect(header.children).toHaveLength(4);
    expect(row.children).toHaveLength(4);
    expect(row.classList.contains('px-2')).toBe(true);
    expect(header.classList.contains('px-2')).toBe(true);
  });

  it('shows a lexicon rule as separate values so phrase and replacement never run together', async () => {
    await open();

    const row = document.body.querySelector('[data-lexicon-row]') as HTMLElement;
    const phrase = row.querySelector('[data-lexicon-phrase]') as HTMLElement;
    const replacement = row.querySelector('[data-lexicon-replacement]') as HTMLElement;
    const kind = row.querySelector('[data-lexicon-kind]') as HTMLElement;

    expect(phrase.textContent?.trim()).toBe('gg');
    expect(replacement.textContent?.trim()).toBe('good game');
    expect(kind.textContent?.trim()).toBe('Whole word, any case');
  });

  it('deletes a lexicon rule only after the confirm', async () => {
    await open();
    expect(document.body.querySelectorAll('[data-lexicon-row]')).toHaveLength(1);

    byLabel('Delete the rule for gg').click();
    await flushPromises();
    expect(sent.some((call) => call.method === 'DELETE')).toBe(false);

    (document.body.querySelector('[data-confirm-action]') as HTMLElement).click();
    await flushPromises();
    expect(sent.some((call) => call.method === 'DELETE')).toBe(true);
  });

  it('plays a rule through the channel voice so the owner hears the replacement', async () => {
    config.defaultVoiceId = 'en-US-AriaNeural';
    await open();

    byLabel('Hear the rule for gg').click();
    await flushPromises();

    const post = sent.find((call) => call.path.endsWith('/tts/test'));

    expect(post?.method).toBe('POST');

    expect(post?.body).toEqual({
      text: 'gg',
      voiceId: 'en-US-AriaNeural',
    });

    config.defaultVoiceId = null;
  });

  it('offers no hear control while no voice is known to speak with', async () => {
    await open();

    expect(byLabel('Hear the rule for gg')).toBeNull();
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

  it('saves the Azure region on its own, without a key', async () => {
    await open();

    const box = byLabel('Azure region');

    expect(box.value).toBe('westeurope');

    box.value = 'northeurope';
    box.dispatchEvent(new Event('input'));
    await flushPromises();
    box.dispatchEvent(new Event('blur'));
    await flushPromises();

    const put = sent.find((call) => call.method === 'PUT' && call.path.endsWith('/tts/config'));

    expect(put?.body).toEqual({ azureRegion: 'northeurope' });
    expect(sent.some((call) => call.path.includes('/byok/'))).toBe(false);
  });

  it('says for each provider whether a key is stored', async () => {
    await open();

    const status = (provider: string): string =>
      document.body
        .querySelector(`[data-key-form="${provider}"] [data-slot="status-badge"]`)
        ?.textContent?.trim() ?? '';

    expect(status('azure')).toBe('Key stored');
    expect(status('elevenlabs')).toBe('No key');
  });

  it('lays every provider out the same: status beside the name, buttons in one group', async () => {
    await open();

    const forms = [...document.body.querySelectorAll('[data-key-form]')];

    expect(forms).toHaveLength(2);

    for (const form of forms) {
      const actions = form.querySelector('[data-key-actions]') as HTMLElement;
      const label = form.querySelector('[data-key-label]') as HTMLElement;

      expect(actions.textContent).toContain('Save key');
      expect(label.className).not.toContain('justify-between');
      expect(label.querySelector('[data-slot="status-badge"]')).not.toBeNull();
    }
  });

  // A-tts defect 5: at 390 the Azure "Save key" sat under the region field instead of next to its key.
  it('puts each key and its own Save in one row, with the Azure region below that row', async () => {
    await open();

    for (const provider of ['azure', 'elevenlabs']) {
      const form = document.body.querySelector(`[data-key-form="${provider}"]`) as HTMLElement;
      const row = form.querySelector('[data-key-row]') as HTMLElement;

      expect(row.contains(form.querySelector('input[type="password"]'))).toBe(true);
      expect(row.contains(form.querySelector('[data-key-actions]'))).toBe(true);
      expect(row.className).not.toContain('flex-wrap');
    }

    const azure = document.body.querySelector('[data-key-form="azure"]') as HTMLElement;
    const region = azure.querySelector('[data-key-region]') as HTMLElement;

    expect(azure.querySelector('[data-key-row]')?.contains(region)).toBe(false);
    expect(region.querySelector('input')).not.toBeNull();

    expect(
      document.body.querySelector('[data-key-form="elevenlabs"] [data-key-region]'),
    ).toBeNull();
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

  it('puts the spelling box, Preview and Save on one row with the help line below', async () => {
    config.defaultVoiceId = 'en-US-AriaNeural';
    await open();

    const row = document.body.querySelector('[data-spelling-row]') as HTMLElement;

    expect(row.querySelector('input')).not.toBeNull();
    expect(row.contains(button('Preview'))).toBe(true);
    expect(row.contains(button('Save spelling'))).toBe(true);
    expect(row.className).toContain('items-center');
    expect(row.contains(document.body.querySelector('[data-spelling-help]'))).toBe(false);
    config.defaultVoiceId = null;
  });

  it('plays the typed spelling through the channel voice without saving it', async () => {
    config.defaultVoiceId = 'en-US-AriaNeural';
    await open();

    const spelling = document.body.querySelector<HTMLInputElement>(
      '[data-spelling-row] input',
    ) as HTMLInputElement;

    spelling.value = 'stoney the eagle';
    spelling.dispatchEvent(new Event('input'));
    await flushPromises();
    button('Preview').click();
    await flushPromises();

    const post = sent.find((call) => call.path.endsWith('/tts/test'));

    expect(post?.body).toEqual({
      text: 'stoney the eagle',
      voiceId: 'en-US-AriaNeural',
    });

    expect(sent.some((call) => call.path.includes('name-pronunciation'))).toBe(false);
    config.defaultVoiceId = null;
  });

  it('previews the plain channel name while the spelling box is empty', async () => {
    config.defaultVoiceId = 'en-US-AriaNeural';
    await open();

    const spelling = document.body.querySelector<HTMLInputElement>(
      '[data-spelling-row] input',
    ) as HTMLInputElement;

    spelling.value = '';
    spelling.dispatchEvent(new Event('input'));
    await flushPromises();
    button('Preview').click();
    await flushPromises();

    expect(sent.find((call) => call.path.endsWith('/tts/test'))?.body).toEqual({
      text: 'Stoney_Eagle',
      voiceId: 'en-US-AriaNeural',
    });

    config.defaultVoiceId = null;
  });
});
