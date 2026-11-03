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
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ref } from 'vue';
import { createMemoryHistory, createRouter } from 'vue-router';

import { i18n } from '@/i18n';

import en from '../i18n/en.json';
import EventResponsesView from '../EventResponsesView.vue';

type Options = { params?: { path?: Record<string, unknown> } };
type Row = Record<string, unknown>;

const posts: string[] = [];
let rows: Row[] = [];
let held: string[] = [];
let result: Row = {};

const ok = (data: unknown) => ({ data: { data }, response: { status: 200, ok: true } });

const response = (eventType: string): Row => ({
  id: `id-${eventType}`,
  eventType,
  isEnabled: true,
  responseType: 'chat_message',
  message: 'Welcome {user}!',
  pipelineId: null,
  metadata: {},
  followsPlatformDefault: false,
  speakWithTts: false,
  toneLines: [],
  speaksInTone: false,
  updatedAt: '2026-10-01T10:00:00Z',
  createdAt: '2026-10-01T10:00:00Z',
});

const sample = (overrides: Row = {}): Row => ({
  eventType: 'channel.raid',
  variables: { user: 'Stoney_Eagle', viewers: '25' },
  chatLine: 'Raid from Stoney_Eagle with 25 viewers!',
  ttsText: 'Stoney Eagle raids with 25 viewers',
  speechLines: [{ text: 'Stoney Eagle raids with 25 viewers', refusedReason: null }],
  alerts: ['Stoney_Eagle is raiding'],
  pipelineSteps: [],
  chatMessagesSent: 1,
  ttsQueued: 1,
  overlaysShown: 1,
  warnings: [],
  ...overrides,
});

function read(path: string, options?: Options) {
  const eventType = options?.params?.path?.eventType;

  if (path.endsWith('/roles/effective/me'))
    return ok({ heldActionKeys: held });

  if (path.endsWith('/pipelines/actions')) {
    return ok({
      actions: [
        {
          type: 'grant_currency',
          category: { key: 'pipeline.category.economy' },
          description: { key: 'x' },
          fields: [],
        },
        {
          type: 'send_message',
          category: { key: 'pipeline.category.chat' },
          description: { key: 'x' },
          fields: [],
        },
      ],
      conditions: [],
    });
  }

  if (path.endsWith('/catalog'))
    return ok([]);

  if (typeof eventType === 'string')
    return ok(rows.find((row) => row.eventType === eventType));

  if (path.endsWith('/event-responses'))
    return ok(rows);

  return ok([]);
}

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string, options?: Options) => read(path, options),
    POST: async (path: string, options?: Options) => {
      posts.push(`${path}|${String(options?.params?.path?.eventType)}`);

      return ok(result);
    },
    PUT: async () => ok(null),
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;

beforeEach(() => {
  posts.length = 0;
  held = ['eventresponses:read', 'eventresponses:write'];
  result = sample();
  rows = [response('channel.follow'), response('channel.raid'), response('channel.cheer')];
  setActivePinia(createPinia());
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

async function settle(): Promise<void> {
  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();
}

async function open(url: string = '/app/event-responses'): Promise<void> {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/app/event-responses', component: { template: '<div />' } }],
  });

  await router.push(url);

  const queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  mounted = mount(EventResponsesView, {
    attachTo: document.body,
    global: { plugins: [i18n, router, [VueQueryPlugin, { queryClient: queries }]] },
  });

  await settle();
}

const testButtons = (): HTMLButtonElement[] =>
  [...document.body.querySelectorAll<HTMLButtonElement>('[data-event-test]')];
const editorButton = (): HTMLButtonElement =>
  document.body.querySelector<HTMLButtonElement>('[data-event-editor] [data-event-test]')!;
// The Test button sits beside the row body, in the same row wrapper.
const rowButton = (eventType: string): HTMLButtonElement | null =>
  document.body
    .querySelector(`[data-event-type="${eventType}"]`)!
    .parentElement!
    .querySelector<HTMLButtonElement>('[data-event-test]');
const resultBox = (): HTMLElement | null => document.body.querySelector('[data-test-result]');
const warnings = (): string[] =>
  [...document.body.querySelectorAll('[data-test-warning]')].map((line) => line.textContent!.trim());
const lines = (selector: string): string[] =>
  [...document.body.querySelectorAll(`[data-test-result] ${selector}`)].map((line) => line.textContent!.trim());

async function press(button: HTMLElement): Promise<void> {
  button.click();
  await settle();
}

describe('Event response test button', () => {
  it('sends the test request for the open event type from the editor header', async () => {
    await open('/app/event-responses?event=channel.raid');
    await press(editorButton());

    expect(posts).toEqual(['/api/v{version}/channels/{channelId}/event-responses/{eventType}/test|channel.raid']);
  });

  it('sends the test request for the row that was pressed, and opens no editor', async () => {
    await open();

    await press(rowButton('channel.cheer')!);

    expect(posts).toEqual(['/api/v{version}/channels/{channelId}/event-responses/{eventType}/test|channel.cheer']);
    expect(document.body.querySelector('[data-event-editor]')).toBeNull();
    expect(resultBox()).not.toBeNull();
  });

  it('shows every event type in the list with a Test button (N of M)', async () => {
    await open();

    const types = [...document.body.querySelectorAll<HTMLElement>('[data-event-row]')].map((row) => row.dataset.eventType);
    const withButton = types.filter((type): boolean => rowButton(type!) !== null);

    expect(types).toHaveLength(3);
    expect(withButton).toEqual(types);
    expect(testButtons()).toHaveLength(3);
  });

  it('shows the chat line, the speech text, the alerts and the steps that ran', async () => {
    result = sample({
      pipelineSteps: [
        {
          index: 0,
          actionType: 'send_message',
          succeeded: true,
        },
        {
          index: 1,
          actionType: 'play_sound',
          succeeded: false,
          errorMessage: 'No sound set',
        },
      ],
    });

    await open('/app/event-responses?event=channel.raid');
    await press(editorButton());

    expect(lines('[data-test-chat]')).toEqual(['Raid from Stoney_Eagle with 25 viewers!']);
    expect(lines('[data-test-tts]')).toEqual(['Stoney Eagle raids with 25 viewers']);
    expect(lines('[data-test-alert]')).toEqual(['Stoney_Eagle is raiding']);

    expect(lines('[data-test-step]')).toEqual([
      expect.stringContaining('send_message'),
      expect.stringContaining('No sound set'),
    ]);
  });

  it('says plainly when the response said or sent nothing', async () => {
    result = sample({
      chatLine: null,
      ttsText: null,
      speechLines: [],
      alerts: [],
    });

    await open('/app/event-responses?event=channel.raid');
    await press(editorButton());

    expect(lines('[data-test-chat]')).toEqual([]);
    expect(resultBox()!.textContent).toContain('No chat line');
    expect(resultBox()!.textContent).toContain('No speech');
    expect(resultBox()!.textContent).toContain('No alerts');
  });

  it('shows the reason under a speech line the speech service refused', async () => {
    result = sample({
      speechLines: [{ text: 'Raid by Stoney_Eagle', refusedReason: 'TTS on this channel needs at least 500 bits.' }],
    });

    await open('/app/event-responses?event=channel.raid');
    await press(editorButton());

    expect(lines('[data-test-tts]')).toEqual(['Raid by Stoney_Eagle']);
    expect(lines('[data-test-tts-refused]')).toEqual(['TTS on this channel needs at least 500 bits.']);
    expect(resultBox()!.textContent).not.toContain('No speech');
  });

  it('shows no refusal line for a speech line that was accepted', async () => {
    await open('/app/event-responses?event=channel.raid');
    await press(editorButton());

    expect(lines('[data-test-tts]')).toEqual(['Stoney Eagle raids with 25 viewers']);
    expect(lines('[data-test-tts-refused]')).toEqual([]);
  });

  it('lists the lines the send_message steps sent and the speech of a pipeline response', async () => {
    result = sample({
      chatLine: null,
      speechLines: [{ text: null, refusedReason: 'The TTS provider returned no audio.' }],
      pipelineSteps: [
        {
          index: 0,
          actionType: 'send_message',
          succeeded: true,
          output: 'Stoney_Eagle just followed!',
        },
        {
          index: 1,
          actionType: 'play_tts',
          succeeded: false,
          errorMessage: 'The TTS provider returned no audio.',
        },
      ],
    });

    await open('/app/event-responses?event=channel.raid');
    await press(editorButton());

    expect(lines('[data-test-chat]')).toEqual(['Stoney_Eagle just followed!']);
    expect(lines('[data-test-tts]')).toEqual(['A pipeline step read a line aloud.']);
    expect(lines('[data-test-tts-refused]')).toEqual(['The TTS provider returned no audio.']);
    expect(resultBox()!.textContent).not.toContain('No chat line');
  });

  it('says the line goes to chat and is read aloud, not that nothing is sent', async () => {
    await open('/app/event-responses?event=channel.raid');
    await press(editorButton());

    expect(document.body.textContent).toContain('sent to your chat and read aloud');
    expect(document.body.textContent).not.toContain('sends nothing to your chat');
  });

  it('shows a visible line for each warning, before the result', async () => {
    result = sample({ warnings: ['no_overlay_source', 'response_disabled', 'no_pipeline'] });

    await open('/app/event-responses?event=channel.raid');
    await press(editorButton());

    const found = warnings();

    expect(found).toHaveLength(3);
    expect(found[0]).toContain('Open your overlay');
    expect(found[1]).toContain('switched off');
    expect(found[1]).toContain('Enabled');
    expect(found[2]).toContain('no pipeline');

    const first = document.body.querySelector('[data-test-warning]')!;
    const chat = document.body.querySelector('[data-test-chat]')!;

    expect(first.compareDocumentPosition(chat) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('shows no warning line when the test raised none', async () => {
    await open('/app/event-responses?event=channel.raid');
    await press(editorButton());

    expect(warnings()).toEqual([]);
  });

  it('tells the person a currency step runs for real, naming the step', async () => {
    result = sample({
      pipelineSteps: [
        {
          index: 0,
          actionType: 'send_message',
          succeeded: true,
        },
        {
          index: 1,
          actionType: 'grant_currency',
          succeeded: true,
        },
      ],
    });

    await open('/app/event-responses?event=channel.raid');
    await press(editorButton());

    const line = document.body.querySelector('[data-test-economy]');

    expect(line?.textContent).toContain('for real');
    expect(line?.textContent).toContain('grant_currency');
    expect(line?.textContent).not.toContain('send_message');
  });

  it('shows no economy line when no step is an economy step', async () => {
    result = sample({ pipelineSteps: [{
      index: 0,
      actionType: 'send_message',
      succeeded: true,
    }] });

    await open('/app/event-responses?event=channel.raid');
    await press(editorButton());

    expect(resultBox()).not.toBeNull();
    expect(document.body.querySelector('[data-test-economy]')).toBeNull();
  });

  it('shows a person who can only read the list and no Test button', async () => {
    held = ['eventresponses:read'];

    await open('/app/event-responses?event=channel.raid');

    expect(document.body.querySelectorAll('[data-event-row]')).toHaveLength(3);
    expect(testButtons()).toEqual([]);
  });
});
