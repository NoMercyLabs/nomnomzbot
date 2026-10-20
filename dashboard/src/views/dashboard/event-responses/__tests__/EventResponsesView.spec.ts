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

import { eventResponsesKey } from '@/api/eventResponses';
import { chooseTarget, openTargetPicker } from '@/components/layout/__tests__/targetPickerDom';
import { i18n } from '@/i18n';

import en from '../i18n/en.json';
import EventResponsesView from '../EventResponsesView.vue';

type Options = {
  params?: { path?: Record<string, unknown> };
  body?: Record<string, unknown>;
};
type Call = {
  method: string;
  path: string;
  eventType: unknown;
  body: Record<string, unknown> | undefined;
};
type Answer = {
  data?: unknown;
  error?: unknown;
  response: { status: number; ok: boolean };
};
type Row = Record<string, unknown>;

const calls: Call[] = [];
let rows: Row[] = [];
let refusePut: string | null = null;
let held: string[] = ['eventresponses:read', 'eventresponses:write'];

const ok = (data: unknown): Answer => ({ data: { data }, response: { status: 200, ok: true } });

const response = (eventType: string, overrides: Row = {}) => ({
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
  ...overrides,
}) as const satisfies Row;

function read(path: string, options?: Options): Answer {
  const eventType = options?.params?.path?.eventType;

  if (path.endsWith('/roles/effective/me'))
    return ok({ heldActionKeys: held });

  if (path.endsWith('/catalog'))
    return ok([{
      eventType: 'channel.follow',
      defaultTemplate: 'x',
      variables: ['user'],
    }]);

  if (typeof eventType === 'string')
    return ok(rows.find((row) => row.eventType === eventType));

  if (path.endsWith('/event-responses'))
    return ok(rows);

  if (path.endsWith('/commands'))
    return ok([{ id: 'c1', name: 'hello' }]);

  return ok([]);
}

function write(method: string, path: string, options?: Options): Answer {
  const eventType = options?.params?.path?.eventType;

  calls.push({
    method,
    path,
    eventType,
    body: options?.body,
  });

  if (method === 'PUT' && refusePut)
    return { error: { message: refusePut }, response: { status: 400, ok: false } };

  if (method === 'PUT') {
    rows = rows.map((row) =>
      row.eventType === eventType
        ? {
            ...row,
            ...options?.body,
            followsPlatformDefault: false,
          }
        : row);
  }

  if (method === 'POST' && path.endsWith('/reset')) {
    rows = rows.map((row) =>
      row.eventType === eventType
        ? {
            ...row,
            followsPlatformDefault: true,
            message: 'Default hello',
          }
        : row);
  }

  return { response: { status: 200, ok: true } };
}

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string, options?: Options) => read(path, options),
    POST: async (path: string, options?: Options) => write('POST', path, options),
    PUT: async (path: string, options?: Options) => write('PUT', path, options),
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;
let router: Router;
let pinia: Pinia;
let queries: QueryClient;

beforeEach(() => {
  localStorage.clear();
  calls.length = 0;
  held = ['eventresponses:read', 'eventresponses:write'];

  rows = [
    response('channel.follow'),
    response('channel.raid', {
      isEnabled: false,
      message: 'Raid from {user}',
      followsPlatformDefault: true,
    }),
  ];

  refusePut = null;
  pinia = createPinia();
  setActivePinia(pinia);
  queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });
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
  router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/app/event-responses',
        name: 'dashboard-event-responses',
        component: { template: '<div />' },
      },
    ],
  });

  await router.push(url);

  mounted = mount(EventResponsesView, {
    attachTo: document.body,
    global: { plugins: [i18n, pinia, router, [VueQueryPlugin, { queryClient: queries }]] },
  });

  await settle();
}

const rowTypes = (): (string | undefined)[] =>
  [...document.body.querySelectorAll<HTMLElement>('[data-event-row]')].map(
    (row) => row.dataset.eventType,
  );
const rowOf = (eventType: string): HTMLElement =>
  document.body.querySelector(`[data-event-type="${eventType}"]`) as HTMLElement;
const editor = (): HTMLElement | null => document.body.querySelector('[data-event-editor]');
const buttonByText = (label: string): HTMLButtonElement =>
  [...document.body.querySelectorAll<HTMLButtonElement>('button')].find(
    (b) => b.textContent?.trim() === label,
  ) as HTMLButtonElement;
const switchOf = (title: string): HTMLButtonElement =>
  [...document.body.querySelectorAll<HTMLElement>('[data-slot="setting-row"], label')]
    .find((row) => row.textContent?.includes(title))
    ?.querySelector('button[role="switch"]') as HTMLButtonElement;
const messageBox = (): HTMLTextAreaElement =>
  editor()!.querySelector('textarea') as HTMLTextAreaElement;
const replyLines = (): (string | undefined)[] =>
  [...editor()!.querySelectorAll<HTMLElement>('[data-reply-line] [data-reply-text]')].map(
    (line) => line.textContent?.trim(),
  );
const writes = (method: string): Call[] => calls.filter((call) => call.method === method);

async function type(el: HTMLTextAreaElement, value: string): Promise<void> {
  el.value = value;
  el.dispatchEvent(new Event('input', { bubbles: true }));
  await settle();
}

async function click(el: HTMLElement): Promise<void> {
  el.click();
  await settle();
}

describe('Event responses page', () => {
  it('lists every event type of the channel', async () => {
    await open();

    expect(rowTypes()).toEqual(['channel.follow', 'channel.raid']);
  });

  it('opens an event in the editor with its saved message', async () => {
    await open();
    await click(rowOf('channel.raid'));

    expect(editor()).not.toBeNull();
    expect(messageBox().value).toBe('Raid from {user}');
  });

  it('turns an event off at once from the switch', async () => {
    await open();
    await click(rowOf('channel.follow'));
    await click(switchOf('Enabled'));

    expect(writes('PUT').map((call) => [call.eventType, call.body])).toEqual([
      ['channel.follow', { isEnabled: false }],
    ]);

    expect(rowOf('channel.follow').textContent).toContain('Off');
  });

  it('saves only the changed message', async () => {
    await open();
    await click(rowOf('channel.follow'));
    await type(messageBox(), 'Hi {user}, welcome in');
    await click(buttonByText('Save changes'));

    expect(writes('PUT')).toHaveLength(1);
    expect(writes('PUT')[0]!.body).toEqual({ message: 'Hi {user}, welcome in' });
  });

  it('shows the message or the pipeline picker, never both', async () => {
    await open();
    await click(rowOf('channel.follow'));
    expect(editor()!.querySelector('textarea')).not.toBeNull();
    expect(editor()!.querySelector('[data-reply-pipeline]')).toBeNull();

    await click(buttonByText('Pipeline'));

    expect(editor()!.querySelector('textarea')).toBeNull();
    expect(editor()!.querySelector('[data-reply-pipeline]')).not.toBeNull();
  });

  it('offers Read it aloud only for a text reply, never for a pipeline', async () => {
    await open();
    await click(rowOf('channel.follow'));
    expect(switchOf('Read it aloud')).toBeDefined();

    await click(buttonByText('Pipeline'));

    expect(switchOf('Read it aloud')).toBeUndefined();
  });

  it('opens a pipeline response in Full mode and saves Simple as a chat message with the pipeline cleared', async () => {
    rows = [response('channel.follow', { responseType: 'pipeline', pipelineId: 'p1' })];

    await open();
    await click(rowOf('channel.follow'));
    expect(editor()!.querySelector('textarea')).toBeNull();

    await click(buttonByText('Simple reply'));
    expect(messageBox().value).toBe('Welcome {user}!');

    await click(buttonByText('Save changes'));

    expect(writes('PUT')[0]!.body).toEqual({
      responseType: 'chat_message',
      pipelineId: '00000000-0000-0000-0000-000000000000',
    });
  });

  it('saves a chat message switched to Full pipeline as the pipeline type with its pipeline, and no message', async () => {
    rows = [response('channel.follow', { pipelineId: 'p1' })];

    await open();
    await click(rowOf('channel.follow'));
    await click(buttonByText('Pipeline'));
    await click(buttonByText('Save changes'));

    expect(writes('PUT')[0]!.body).toEqual({
      responseType: 'pipeline',
      target: { kind: 1, id: 'p1' },
    });
  });

  it('shows a command the response is bound to when it opens in the editor', async () => {
    rows = [response('channel.follow', {
      responseType: 'pipeline',
      target: { kind: 0, id: 'c1' },
    })];

    await open();
    await click(rowOf('channel.follow'));

    expect(editor()!.querySelector('[data-target-picker]')!.textContent).toContain('!hello');
  });

  it('sends a command target when a command is chosen instead of a pipeline', async () => {
    Element.prototype.hasPointerCapture = (): boolean => false;
    Element.prototype.releasePointerCapture = (): void => undefined;
    Element.prototype.scrollIntoView = (): void => undefined;

    await open();
    await click(rowOf('channel.follow'));
    await click(buttonByText('Pipeline'));

    await openTargetPicker(editor()!);
    await chooseTarget('!hello');
    await settle();
    await click(buttonByText('Save changes'));

    expect(writes('PUT')[0]!.body).toEqual({
      responseType: 'pipeline',
      target: { kind: 0, id: 'c1' },
    });
  });

  it('keeps the draft and shows the reason when the bot refuses the save', async () => {
    refusePut = 'Message too long';
    await open();
    await click(rowOf('channel.follow'));
    await type(messageBox(), 'Changed');
    await click(buttonByText('Save changes'));

    expect(editor()!.querySelector('[role="alert"]')?.textContent).toContain('Message too long');
    expect(messageBox().value).toBe('Changed');
  });

  it('restores the platform default through the reset call', async () => {
    await open();
    await click(rowOf('channel.follow'));
    await click(buttonByText('Reset to default'));

    expect(writes('POST')).toHaveLength(0);
    expect(document.body.textContent).toContain('Welcome {user}!');

    await click(document.body.querySelector<HTMLButtonElement>('[data-confirm-action]')!);

    expect(writes('POST').map((call) => [call.eventType, call.path.endsWith('/reset')])).toEqual([
      ['channel.follow', true],
    ]);

    expect(messageBox().value).toBe('Default hello');
  });

  it('shows no control that cannot work', async () => {
    await open();
    await click(rowOf('channel.follow'));

    expect(buttonByText('Send a test')).toBeUndefined();
    expect(editor()!.textContent).not.toContain('Not available yet');
    expect(editor()!.querySelector('[data-helper-toggle]')).toBeNull();
  });

  it('shows an event another dashboard added once the live signal marks the list stale', async () => {
    await open();

    rows.push(response('channel.cheer'));
    await queries.invalidateQueries({ queryKey: eventResponsesKey('c1') });
    await settle();

    expect(rowTypes()).toContain('channel.cheer');
  });
});

describe('Event responses permission gating', () => {
  it('shows a reader the event list and no editor for the open event', async () => {
    held = ['eventresponses:read'];
    await open('/app/event-responses?event=channel.follow');

    expect(rowTypes()).toContain('channel.follow');
    expect(editor()).toBeNull();
  });

  it('shows a person with eventresponses:write the editor for the open event', async () => {
    await open('/app/event-responses?event=channel.follow');

    expect(editor()).not.toBeNull();
  });

  it('names an event in words, not from its key', async () => {
    const redemption = 'channel.channel_points_custom_reward_redemption.add';

    rows = [response(redemption)];
    await open();

    expect(rowOf(redemption).textContent).toContain('Reward redeemed');
    expect(rowOf(redemption).textContent).not.toContain('Channel points custom');
  });

  it('lists the lines the bot picks from while the event follows the default', async () => {
    rows = [
      response('channel.raid', {
        followsPlatformDefault: true,
        toneLines: ['Raid from {user}!', 'Here comes {user}!'],
      }),
    ];

    await open('/app/event-responses?event=channel.raid');

    expect(replyLines()).toEqual(['Raid from {user}!', 'Here comes {user}!']);
    expect(editor()!.querySelector('[data-reply-source]')?.textContent).toContain('Platform default');
    expect(editor()!.querySelector('textarea')).toBeNull();
  });

  it('starts a line of the channel own from the default line that was picked, and saves it', async () => {
    rows = [
      response('channel.raid', {
        followsPlatformDefault: true,
        message: null,
        toneLines: ['Raid from {user}!', 'Here comes {user}!'],
      }),
    ];

    await open('/app/event-responses?event=channel.raid');
    await click(editor()!.querySelectorAll<HTMLButtonElement>('[data-reply-line] button')[1]!);

    expect(messageBox().value).toBe('Here comes {user}!');
    expect(editor()!.textContent).toContain('replaces the 2 default lines');

    await type(messageBox(), 'Here comes {user} and friends!');
    await click(buttonByText('Save changes'));

    expect(writes('PUT').map((call) => call.body)).toEqual([
      { message: 'Here comes {user} and friends!' },
    ]);
  });

  it('puts the default lines back when the picked line is discarded', async () => {
    rows = [
      response('channel.raid', {
        followsPlatformDefault: true,
        message: null,
        toneLines: ['Raid from {user}!', 'Here comes {user}!'],
      }),
    ];

    await open('/app/event-responses?event=channel.raid');
    await click(editor()!.querySelector<HTMLButtonElement>('[data-reply-line] button')!);
    await click(buttonByText('Discard'));

    expect(replyLines()).toEqual(['Raid from {user}!', 'Here comes {user}!']);
    expect(writes('PUT')).toHaveLength(0);
  });

  it('shows the line of the channel own as the one editable line, not as a default', async () => {
    rows = [response('channel.follow', { toneLines: [] })];

    await open('/app/event-responses?event=channel.follow');

    expect(replyLines()).toEqual([]);
    expect(messageBox().value).toBe('Welcome {user}!');
    expect(editor()!.querySelector('[data-reply-source]')?.textContent).toContain('Your own text');
  });
});
