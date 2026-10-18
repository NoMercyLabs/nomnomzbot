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
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, h, ref } from 'vue';

import { useBuiltins, type BuiltinCommand } from '@/api/commands';
import { i18n } from '@/i18n';

import BuiltinEditor from '../components/BuiltinEditor.vue';
import en from '../i18n/en.json';

type Options = { params?: { path?: Record<string, unknown> }; body?: Record<string, unknown> };
type Row = Record<string, unknown>;
type Call = {
  method: string;
  path: string;
  key: unknown;
  slot: unknown;
  body: Record<string, unknown> | undefined;
};

const calls: Call[] = [];
let builtins: Row[] = [];
let replies: Row[] = [];

type Answer = { data: { data: unknown }; response: { status: number; ok: boolean } };

const ok = (data: unknown): Answer => ({
  data: { data },
  response: { status: 200, ok: true },
});

const builtin = (overrides: Row = {}) => ({
  builtinKey: 'uptime',
  name: '!uptime',
  isEnabled: true,
  isReserved: false,
  speakWithTts: false,
  replyGroup: 'uptime',
  replyOverrideCount: 0,
  defaultCooldownSeconds: 5,
  defaultUserCooldownSeconds: 10,
  defaultMinPermissionLevel: 'Everyone',
  cooldownSecondsOverride: null,
  userCooldownSecondsOverride: null,
  minPermissionLevelOverride: null,
  ...overrides,
}) as const satisfies Row;

const slot = (overrides: Row = {}): Row => ({
  builtinKey: 'uptime',
  slot: 'online',
  label: { key: 'uptime.online', text: 'Online reply' },
  description: { key: 'uptime.online.d', text: 'When the stream is live' },
  effectiveTemplate: 'Live for {uptime}',
  source: 'default',
  defaultTemplate: 'Live for {uptime}',
  toneVariations: [],
  variables: [
    {
      name: 'uptime',
      description: { key: 'v.uptime', text: 'How long the stream runs' },
      sampleValue: '1h',
    },
  ],
  isOverridden: false,
  isLocked: false,
  ...overrides,
});

const group = (slots: Row[]) => ({
  builtinKey: 'uptime',
  commandKeys: ['uptime'],
  replies: slots,
}) as const satisfies Row;

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string) => {
      if (path.endsWith('/builtins/replies'))
        return ok(replies.map((entry) => ({ ...entry })));

      if (path.endsWith('/builtins'))
        return ok(builtins.map((entry) => ({ ...entry })));

      return ok(null);
    },
    PUT: async (path: string, options?: Options) => write('PUT', path, options),
    PATCH: async (path: string, options?: Options) => write('PATCH', path, options),
    DELETE: async (path: string, options?: Options) => write('DELETE', path, options),
  },
}));

function write(
  method: string,
  path: string,
  options?: Options,
): { response: { status: number; ok: boolean } } {
  calls.push({
    method,
    path,
    key: options?.params?.path?.builtinKey,
    slot: options?.params?.path?.slot,
    body: options?.body,
  });

  if (path.endsWith('/settings') && method === 'PUT') {
    builtins[0] = {
      ...builtins[0],
      cooldownSecondsOverride: options?.body?.cooldownSeconds ?? null,
      userCooldownSecondsOverride: options?.body?.userCooldownSeconds ?? null,
      minPermissionLevelOverride: options?.body?.minPermissionLevel ?? null,
    };
  }

  return { response: { status: 200, ok: true } };
}

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

const Harness = defineComponent({
  setup() {
    const rows = useBuiltins('c1');

    return () => {
      const row = rows.data.value?.[0] as BuiltinCommand | undefined;

      return row
        ? h(BuiltinEditor, { builtin: row })
        : null;
    };
  },
});

let mounted: VueWrapper | null = null;

const settle = async (): Promise<void> => {
  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();
};

async function open(): Promise<void> {
  const pinia = createPinia();

  setActivePinia(pinia);

  mounted = mount(Harness, {
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

  await settle();
}

const editor = (): HTMLElement =>
  document.body.querySelector<HTMLElement>('[data-builtin-editor]') as HTMLElement;
const input = (label: string): HTMLInputElement =>
  editor().querySelector<HTMLInputElement>(`[aria-label="${label}"]`) as HTMLInputElement;
const button = (label: string): HTMLButtonElement =>
  [...editor().querySelectorAll<HTMLButtonElement>('button')].find(
    (item) => item.textContent?.trim() === label || item.getAttribute('aria-label') === label,
  ) as HTMLButtonElement;
const replyBox = (): HTMLTextAreaElement =>
  editor().querySelector<HTMLTextAreaElement>('textarea') as HTMLTextAreaElement;
const writes = (): Call[] => calls.filter((call) => call.method !== 'GET');

async function type(el: HTMLInputElement | HTMLTextAreaElement, value: string): Promise<void> {
  el.value = value;
  el.dispatchEvent(new Event('input', { bubbles: true }));
  await settle();
}

async function click(el: HTMLElement): Promise<void> {
  el.click();
  await settle();
}

beforeEach(() => {
  calls.length = 0;
  builtins = [builtin()];
  replies = [group([slot()])];
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

describe('BuiltinEditor', () => {
  it('shows every setting with the catalog defaults, view-only, before Customize', async () => {
    await open();

    expect(input('Chat cooldown (seconds)').value).toBe('5');
    expect(input('Chat cooldown (seconds)').disabled).toBe(true);
    expect(input('Per-person cooldown (seconds)').value).toBe('10');
    expect(input('Per-person cooldown (seconds)').disabled).toBe(true);
    expect(editor().querySelector<HTMLButtonElement>('[role="combobox"]')!.disabled).toBe(true);
    expect(replyBox().value).toBe('Live for {uptime}');
    expect(replyBox().disabled).toBe(true);
    expect(editor().textContent).toContain('Online reply');
    expect(editor().textContent).toContain('{uptime}');
    expect(button('Customize')).toBeDefined();
    expect(button('Save changes')).toBeUndefined();
    expect(writes()).toEqual([]);
  });

  it('makes the fields editable after Customize and says each one overrides the default', async () => {
    await open();
    await click(button('Customize'));

    expect(input('Chat cooldown (seconds)').disabled).toBe(false);
    expect(replyBox().disabled).toBe(false);
    expect(button('Customize')).toBeUndefined();

    await type(input('Chat cooldown (seconds)'), '30');

    expect(editor().textContent).toContain('Overrides the default (5)');
    expect(writes()).toEqual([]);
  });

  it('saves the exact settings, reply and read-aloud bodies, and the cache follows', async () => {
    await open();
    await click(button('Customize'));
    await type(input('Chat cooldown (seconds)'), '30');
    await type(replyBox(), 'Up for {uptime} now');
    await click(editor().querySelector<HTMLButtonElement>('[role="switch"][aria-label="Read it aloud"]')!);
    await click(button('Save changes'));

    const settings = writes().find((call) => call.path.endsWith('/settings'))!;
    const replyCall = writes().find((call) => call.path.endsWith('/replies/{slot}'))!;
    const tts = writes().find((call) => call.path.endsWith('/tts'))!;

    expect(settings.method).toBe('PUT');
    expect(settings.key).toBe('uptime');

    expect(settings.body).toEqual({
      cooldownSeconds: 30,
      userCooldownSeconds: null,
      minPermissionLevel: null,
    });

    expect(replyCall.method).toBe('PUT');
    expect(replyCall.slot).toBe('online');
    expect(replyCall.body).toEqual({ template: 'Up for {uptime} now' });
    expect(tts.method).toBe('PUT');
    expect(tts.body).toEqual({ enabled: true });
    expect(editor().textContent).toContain('Overrides the default (5)');
  });

  it('sends the server reset shapes: a null field in the settings body, DELETE for a reply', async () => {
    builtins = [builtin({ cooldownSecondsOverride: 30, replyOverrideCount: 1 })];

    replies = [
      group([slot({ effectiveTemplate: 'Own words', isOverridden: true })]),
    ];

    await open();

    expect(button('Customize')).toBeUndefined();
    expect(input('Chat cooldown (seconds)').value).toBe('30');
    expect(replyBox().value).toBe('Own words');

    await click(button('Back to default: Chat cooldown (seconds)'));
    const settings = writes().find((call) => call.path.endsWith('/settings'))!;

    expect(settings.method).toBe('PUT');

    expect(settings.body).toEqual({
      cooldownSeconds: null,
      userCooldownSeconds: null,
      minPermissionLevel: null,
    });

    await click(button('Back to default: Online reply'));
    const reset = writes().find((call) => call.path.endsWith('/replies/{slot}') && call.method === 'DELETE')!;

    expect(reset.slot).toBe('online');
    expect(reset.key).toBe('uptime');
  });

  it('keeps a reserved built-in locked with its reason, and a locked reply view-only', async () => {
    builtins = [builtin({ isReserved: true })];
    await open();

    expect(editor().textContent).toContain('data-rights command');
    expect(button('Customize').disabled).toBe(true);
    expect(input('Chat cooldown (seconds)').disabled).toBe(true);

    mounted!.unmount();
    document.body.innerHTML = '';
    builtins = [builtin({ cooldownSecondsOverride: 30 })];
    replies = [group([slot({ isLocked: true })])];
    await open();

    expect(input('Chat cooldown (seconds)').disabled).toBe(false);
    expect(replyBox().disabled).toBe(true);
  });
});
