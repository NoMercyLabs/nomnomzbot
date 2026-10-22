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
import { ref, type Plugin } from 'vue';

import type { NowPlaying } from '@/api/music';
import { i18n } from '@/i18n';
import { STREAM_DECK_MUSIC_ACTIONS } from '@/lib/streamDeckActions';

import PlayerButtonPicker from '../components/PlayerButtonPicker.vue';
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

// The strip hands its drop handler to Sortable; a test drops a button by calling it.
type Drop = {
  oldIndex: number;
  newIndex: number;
  item: HTMLElement;
  from: HTMLElement;
};

const sortable = vi.hoisted(() =>
  ({ onUpdate: undefined as ((event: Drop) => void) | undefined }));

vi.mock('@vueuse/integrations/useSortable', () => ({
  useSortable: (_target: unknown, _list: unknown, options: { onUpdate: (event: Drop) => void }) => {
    sortable.onUpdate = options.onUpdate;
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
    actionType: 'music_save_track',
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
      type: 'music_next',
      category: { key: 'music', text: 'Music' },
      description: { key: 'y' },
      fields: [],
    },
    {
      type: 'music_set_volume',
      category: { key: 'music', text: 'Music' },
      description: { key: 'z' },
      fields: [field('volume', 'number')],
    },
    {
      type: 'music_transfer_device',
      category: { key: 'music', text: 'Music' },
      description: { key: 'w' },
      fields: [field('deviceId', 'text')],
    },
  ],
  conditions: [],
};

const devices = [
  {
    id: 'd1',
    name: 'Desk PC',
    type: 'Computer',
    isActive: true,
    volumePercent: 40,
  },
];

function field(name: string, kind: string) {
  return {
    name,
    kind,
    required: true,
    repeatable: false,
    description: null,
    templated: false,
    declaresVariable: false,
  } as const satisfies object;
}

let mounted: VueWrapper | null = null;

function answer(path: string): unknown {
  if (path.endsWith('/pipelines/actions'))
    return catalog;

  if (path.endsWith('/music/devices'))
    return devices;

  return saved;
}

function reply(path: string): unknown {
  const data = answer(path);

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

function plugins(): (Plugin | [Plugin, ...unknown[]])[] {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  return [i18n, createPinia(), [VueQueryPlugin, { queryClient }]];
}

async function open(keys: string[]): Promise<VueWrapper> {
  held.keys = keys;

  mounted = mount(PlayerButtons, {
    props: { track },
    attachTo: document.body,
    global: { plugins: plugins() },
  });

  await flushPromises();

  return mounted;
}

// The panel header's Add button opens the picker with no button chosen to edit.
async function openAdd(): Promise<void> {
  mounted = mount(PlayerButtonPicker, {
    props: {
      open: true,
      buttons: saved,
      index: null,
    },
    attachTo: document.body,
    global: { plugins: plugins() },
  });

  await flushPromises();
}

const editor = ['music:queue:read', 'music:config:write'];

async function press(selector: string): Promise<void> {
  document.querySelector<HTMLElement>(selector)?.click();
  await flushPromises();
}

async function submit(text: string): Promise<void> {
  const form = document.querySelector<HTMLElement>('[data-player-button-picker]');
  const primary = [...(form?.querySelectorAll('button') ?? [])].find((button) =>
    button.textContent?.trim() === text);

  primary?.click();
  await flushPromises();
}

async function editFirst(): Promise<void> {
  const first = document.querySelector<HTMLElement>('[data-player-button]');

  first?.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true }));
  await flushPromises();
}

async function typeLabel(value: string): Promise<void> {
  const input = document.querySelector<HTMLInputElement>('[data-button-label]');

  if (!input)
    throw new Error('no label field');

  input.value = value;
  input.dispatchEvent(new Event('input', { bubbles: true }));
  await flushPromises();
}

function sentBody(): object[] | undefined {
  return held.put.mock.calls[0]?.[1].body;
}

describe('the player button picker', () => {
  it('has no Edit buttons link under the controls any more', async () => {
    await open(editor);

    expect(document.querySelector('[data-player-buttons-edit]')).toBeNull();
  });

  it('offers every Stream Deck music plugin button, in the plugin order', async () => {
    await openAdd();

    const offered = [...document.querySelectorAll<HTMLElement>('[data-deck-action]')].map((item) =>
      item.dataset.deckAction);

    expect(offered).toEqual(STREAM_DECK_MUSIC_ACTIONS.map((action) =>
      action.key));
  });

  it('adds the picked plugin button with its name and icon at the end of the list', async () => {
    await openAdd();
    await press('[data-deck-action="music-next"]');
    await submit('Add button');

    expect(sentBody()).toEqual([
      ...saved,
      {
        id: '',
        label: 'Next Song',
        icon: 'next',
        actionType: 'music_next',
        parameters: {},
      },
    ]);
  });

  it('starts a picked plugin button with the plugin default setting', async () => {
    await openAdd();
    await press('[data-deck-action="music-set-volume"]');

    const volume = document.querySelector<HTMLInputElement>('[data-step-field="volume"] input');

    expect(volume?.value).toBe('50');

    await submit('Add button');

    expect(sentBody()?.at(-1)).toMatchObject({
      actionType: 'music_set_volume',
      parameters: { volume: 50 },
    });
  });

  it('picks the playback device from the music account devices, not a typed id', async () => {
    await openAdd();
    await press('[data-deck-action="music-transfer-device"]');

    const device = document.querySelector<HTMLElement>('[data-step-field="deviceId"]');

    expect(device?.querySelector('input')).toBeNull();
    expect(device?.querySelector('[role="combobox"]')).not.toBeNull();
    expect(device?.textContent).toContain('Pick a device');
  });

  it('shows the buttons while nothing is playing', async () => {
    held.keys = editor;

    mounted = mount(PlayerButtons, {
      props: { track: null },
      attachTo: document.body,
      global: { plugins: plugins() },
    });

    await flushPromises();

    const labels = [...document.querySelectorAll('[data-player-button]')].map((button) =>
      button.textContent?.trim());

    expect(labels).toEqual(['Thank them', 'Save song']);
  });

  it('edits a button on a right click and saves it in its own place', async () => {
    await open(editor);
    await editFirst();

    const input = document.querySelector<HTMLInputElement>('[data-button-label]');

    if (!input)
      throw new Error('no label field');

    input.value = 'Thanks!';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await flushPromises();
    await submit('Save button');

    expect(sentBody()).toEqual([{ ...saved[0], label: 'Thanks!' }, saved[1]]);
  });

  it('saves a button with no label as an icon-only button and shows no error', async () => {
    await open(editor);
    await editFirst();
    await typeLabel('');
    await submit('Save button');

    expect(document.querySelector('[data-problem="label"]')).toBeNull();
    expect(sentBody()).toEqual([{ ...saved[0], label: '' }, saved[1]]);
  });

  it('refuses a label of 25 characters, says the limit and sends nothing', async () => {
    await open(editor);
    await editFirst();
    await typeLabel('a'.repeat(25));
    await submit('Save button');

    const problem = document.querySelector('[data-problem="label"]');

    expect(problem?.textContent?.trim()).toBe('Use at most 24 characters.');
    expect(held.put).not.toHaveBeenCalled();
  });

  it('says in the label field that the label is optional', async () => {
    await open(editor);
    await editFirst();

    const input = document.querySelector<HTMLInputElement>('[data-button-label]');

    expect(input?.placeholder).toBe('Label (optional)');
  });

  it('removes a button from the edit dialog', async () => {
    await open(editor);
    await editFirst();
    await press('[data-button-remove]');

    expect(sentBody()).toEqual([saved[1]]);
  });

  it('saves the order a button was dragged to', async () => {
    await open(editor);

    const strip = document.querySelector<HTMLElement>('[data-player-buttons]');
    const first = document.querySelector<HTMLElement>('[data-player-button]');

    if (!strip || !first)
      throw new Error('no strip');

    strip.append(first);

    sortable.onUpdate?.({
      oldIndex: 0,
      newIndex: 1,
      item: first,
      from: strip,
    });

    await flushPromises();

    expect(sentBody()).toEqual([saved[1], saved[0]]);
  });

  it('opens no editor for a viewer who may not change the buttons', async () => {
    await open(['music:queue:read']);
    await editFirst();

    expect(document.querySelector('[data-player-button-picker]')).toBeNull();
  });
});
