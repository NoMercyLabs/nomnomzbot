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
import { ref } from 'vue';

import type { NowPlaying } from '@/api/music';
import { i18n } from '@/i18n';

import PlayerButtons from '../components/PlayerButtons.vue';
import en from '../i18n/en.json';

type PutInit = { body: object[] };

const held = vi.hoisted(() => ({
  keys: [] as string[],
  get: vi.fn<(path: string) => unknown>(),
  put: vi.fn<(path: string, init: PutInit) => unknown>(),
}));

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({
    known: ref(true),
    can: (key: string): boolean => held.keys.includes(key),
  }),
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

vi.mock('@/api/client', () => ({
  client: {
    GET: held.get,
    PUT: held.put,
    POST: vi.fn<() => void>(),
    use: vi.fn<() => void>(),
  },
}));

const track = { trackName: 'Midnight City', artist: 'M83' } as unknown as NowPlaying;

const saved = [
  {
    id: 'b1',
    label: 'Thank them',
    icon: 'heart',
    actionType: 'send_message',
    parameters: {},
  },
  {
    id: 'b2',
    label: 'Save song',
    icon: 'bookmark-add',
    actionType: 'save_track',
    parameters: {},
  },
];

const catalog = {
  actions: [
    {
      type: 'send_message',
      category: { key: 'chat', text: 'Chat' },
      description: { key: 'x' },
      fields: [],
    },
    {
      type: 'save_track',
      category: { key: 'music', text: 'Music' },
      description: { key: 'y' },
      fields: [],
    },
  ],
  conditions: [],
};

let mounted: VueWrapper | null = null;

function reply(path: string): unknown {
  const data = path.endsWith('/pipelines/actions')
    ? catalog
    : saved;

  return {
    data: { data },
    error: undefined,
    response: { status: 200 },
  };
}

beforeEach(() => {
  held.keys = [];
  held.get.mockImplementation(reply);

  held.put.mockImplementation((_path: string, init: PutInit) =>
    ({
      data: { data: init.body },
      error: undefined,
      response: { status: 200 },
    }));

  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
  vi.clearAllMocks();
});

async function open(keys: string[]): Promise<VueWrapper> {
  held.keys = keys;

  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  mounted = mount(PlayerButtons, {
    props: { track },
    attachTo: document.body,
    global: { plugins: [i18n, createPinia(), [VueQueryPlugin, { queryClient }]] },
  });

  await flushPromises();

  return mounted;
}

async function openEditor(): Promise<void> {
  await open(['music:queue:read', 'music:config:write']);
  document.querySelector<HTMLElement>('[data-player-buttons-edit]')?.click();
  await flushPromises();
}

function rows(): HTMLElement[] {
  return [...document.querySelectorAll<HTMLElement>('[data-player-button-row]')];
}

function labels(): string[] {
  return rows().map((row) =>
    row.querySelector<HTMLInputElement>('[data-button-label]')?.value ?? '');
}

async function type(row: HTMLElement, text: string): Promise<void> {
  const input = row.querySelector<HTMLInputElement>('[data-button-label]');

  if (!input)
    throw new Error('no label field');

  input.value = text;
  input.dispatchEvent(new Event('input', { bubbles: true }));
  await flushPromises();
}

async function press(selector: string): Promise<void> {
  document.querySelector<HTMLElement>(selector)?.click();
  await flushPromises();
}

async function saveList(): Promise<void> {
  const form = document.querySelector<HTMLElement>('[data-player-buttons-editor]');
  const primary = [...(form?.querySelectorAll('button') ?? [])].find((button) =>
    button.textContent?.trim() === 'Save buttons');

  primary?.click();
  await flushPromises();
}

describe('the player button editor', () => {
  it('is hidden without the music config write permission', async () => {
    await open(['music:queue:read']);

    expect(document.querySelector('[data-player-buttons-edit]')).toBeNull();
  });

  it('opens with the saved buttons in their saved order', async () => {
    await openEditor();

    expect(labels()).toEqual(['Thank them', 'Save song']);
  });

  it('adds a button and saves the whole list in one PUT', async () => {
    await openEditor();
    await press('[data-button-add]');

    const added = rows()[2];

    if (!added)
      throw new Error('no new row');

    await type(added, 'Hype');
    added.querySelector<HTMLElement>('[data-button-action]')?.click();
    await flushPromises();
    document.querySelector<HTMLElement>('[data-action-option="send_message"]')?.click();
    await flushPromises();
    await saveList();

    expect(held.put).toHaveBeenCalledTimes(1);

    const [path, init] = held.put.mock.calls[0] ?? [];

    expect(path).toBe('/api/v{version}/channels/{channelId}/music/player-buttons');

    expect(init?.body).toEqual([
      ...saved,
      {
        id: '',
        label: 'Hype',
        icon: 'lightning',
        actionType: 'send_message',
        parameters: {},
      },
    ]);
  });

  it('sends the buttons in the order the person moved them to', async () => {
    await openEditor();
    await press('[data-player-button-row]:nth-child(2) [data-button-up]');
    await saveList();

    const [, init] = held.put.mock.calls[0] ?? [];

    expect(init?.body.map((button) =>
      (button as { id: string }).id)).toEqual(['b2', 'b1']);
  });

  it('leaves a removed button out of the saved list', async () => {
    await openEditor();
    await press('[data-player-button-row]:nth-child(1) [data-button-remove]');
    await saveList();

    const [, init] = held.put.mock.calls[0] ?? [];

    expect(init?.body).toEqual([saved[1]]);
  });

  it('blocks a 25 character label, says why, and sends nothing', async () => {
    await openEditor();

    const first = rows()[0];

    if (!first)
      throw new Error('no row');

    await type(first, 'x'.repeat(25));
    await saveList();

    expect(held.put).not.toHaveBeenCalled();
    expect(document.querySelector('[data-problem="label"]')?.textContent).toContain('1 to 24');
  });
});
