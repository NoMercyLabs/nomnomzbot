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

import { queryClient } from '@/api/queryClient';
import { i18n } from '@/i18n';

import AdminPlatformDefaultsView from '../AdminPlatformDefaultsView.vue';
import permissions from '../../settings/permissions/i18n/en.json';
import en from '../i18n/en.json';
import { dashboardAdminPlatformDefaultsRoute } from '../route';

type Answer = {
  data?: unknown;
  error?: unknown;
  response: { status: number; ok: boolean };
};

type GetCall = { path: string; query: Record<string, unknown> | undefined };

const gets: GetCall[] = [];
const puts: { path: string; body: Record<string, unknown> }[] = [];
let refused: boolean = false;
let dangerous: boolean = false;
let affected: number = 12;

const action = (key: string, over: Record<string, unknown> = {}) => ({
  actionKey: key,
  plane: 0,
  description: `${key} description`,
  shippedDefaultLevel: 10,
  platformDefaultLevel: null,
  effectiveDefaultLevel: 10,
  floorLevel: 0,
  floorTier: 2,
  channelOverrideCount: 3,
  ...over,
}) as const satisfies Record<string, unknown>;

const actions = [
  action('moderation.ban'),
  action('timers.create', {
    shippedDefaultLevel: 30,
    effectiveDefaultLevel: 20,
    platformDefaultLevel: 20,
    floorLevel: 6,
  }),
];

const builtins = [
  {
    builtinKey: 'lurk',
    slot: 'start',
    shippedTemplate: 'Enjoy the lurk, {user}',
    platformTemplate: null,
    channelsWithOwnReply: 2,
  },
  {
    builtinKey: 'hug',
    slot: 'self',
    shippedTemplate: 'Hug yourself',
    platformTemplate: 'Hug yourself, {user}',
    channelsWithOwnReply: 1,
  },
];

const events = [
  {
    eventType: 'channel.follow',
    isEnabled: true,
    message: 'Welcome {user}',
    variables: ['user'],
    channelsFollowing: 9,
    channelsWithOwnResponse: 4,
    updatedAt: '2026-10-01T10:00:00Z',
    speakWithTts: false,
  },
  {
    eventType: 'channel.raid',
    isEnabled: false,
    message: null,
    variables: ['user', 'viewers'],
    channelsFollowing: 5,
    channelsWithOwnResponse: 0,
    updatedAt: '2026-10-01T10:00:00Z',
    speakWithTts: true,
  },
];

const voiceDefault = {
  voiceId: 'en-US-Aria',
  displayName: 'Aria',
  locale: 'en-US',
  provider: 'azure',
  channelsFollowing: 7,
  channelsWithOwnVoice: 2,
};

const candidate = (voiceId: string, locale: string) => ({
  voiceId,
  displayName: voiceId.split('-').at(-1),
  locale,
  gender: 'Female',
  isDefault: voiceId === voiceDefault.voiceId,
}) as const satisfies Record<string, unknown>;

const candidates = [
  candidate('nl-NL-Colette', 'nl-NL'),
  candidate('en-US-Aria', 'en-US'),
  candidate('en-US-Guy', 'en-US'),
];

const posts: { path: string; body: Record<string, unknown> }[] = [];

// The signed-in admin holds every platform key, so every action button shows.
vi.mock('@/composables/usePlatformCan', () => ({
  usePlatformCan: () => ({ known: { value: true }, can: () => true }),
}));

// The voice play button checks the channel key tts:voice:test; the admin holds it.
vi.mock('@/composables/useCan', () => ({
  useCan: () => ({ known: { value: true }, can: () => true }),
}));

vi.mock('vue-sonner', () => ({
  toast: { success: vi.fn<() => void>(), error: vi.fn<() => void>() },
}));

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (
      path: string,
      options: { params?: { query?: Record<string, unknown> } },
    ): Promise<Answer> => {
      gets.push({ path, query: options.params?.query });

      if (refused)
        return { error: { message: 'no' }, response: { status: 403, ok: false } };

      if (path.includes('/templates/helpers'))
        return { data: { status: 'ok', data: [] }, response: { status: 200, ok: true } };

      if (path.endsWith('/blast-radius')) {
        return {
          data: {
            status: 'ok',
            data: {
              channelsAffected: affected,
              channelsKeepingOwnSetting: 3,
              sampleChannelNames: ['alpha', 'beta'],
              requiresDangerConfirmation: dangerous,
            },
          },
          response: { status: 200, ok: true },
        };
      }

      if (path.endsWith('/tts-voice')) {
        return { data: { status: 'ok', data: voiceDefault }, response: { status: 200, ok: true } };
      }

      const lists: Record<string, unknown[]> = {
        candidates,
        channels: [],
        moderated: [],
        'builtin-replies': builtins,
        'event-responses': events,
      };

      const list = lists[path.split('/').at(-1) ?? ''] ?? actions;

      return { data: { status: 'ok', data: list }, response: { status: 200, ok: true } };
    },
    POST: async (path: string, options: { body: Record<string, unknown> }): Promise<Answer> => {
      posts.push({ path, body: options.body });

      return {
        data: {
          status: 'ok',
          data: {
            channelsAffected: affected,
            channelsKeepingOwnSetting: 3,
            sampleChannelNames: ['alpha', 'beta'],
            requiresDangerConfirmation: false,
          },
        },
        response: { status: 200, ok: true },
      };
    },
    PUT: async (path: string, options: { body: Record<string, unknown> }): Promise<Answer> => {
      puts.push({ path, body: options.body });

      return { data: { status: 'ok', data: actions[0] }, response: { status: 200, ok: true } };
    },
  },
}));

let mounted: VueWrapper | null = null;
let pinia: Pinia;

beforeEach(() => {
  gets.length = 0;
  puts.length = 0;
  posts.length = 0;
  refused = false;
  dangerous = false;
  affected = 12;
  pinia = createPinia();
  setActivePinia(pinia);
  i18n.global.mergeLocaleMessage('en', en);
  i18n.global.mergeLocaleMessage('en', permissions);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
  queryClient.clear();
});

const settle = async (): Promise<void> => {
  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();
};

async function open(): Promise<void> {
  mounted = mount(AdminPlatformDefaultsView, {
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

const row = (key: string): HTMLElement =>
  document.body.querySelector<HTMLElement>(`[data-action="${key}"]`)!;
const applyButton = (): HTMLButtonElement =>
  document.body.querySelector<HTMLButtonElement>('[data-form-action]')!;
const levelSelect = (): HTMLSelectElement =>
  document.body.querySelector<HTMLSelectElement>('[data-level]')!;
const preview = (): HTMLElement | null =>
  document.body.querySelector<HTMLElement>('[data-preview]');

async function edit(key: string): Promise<void> {
  row(key).querySelector<HTMLElement>('[data-edit]')!.click();
  await settle();
}

async function choose(value: string): Promise<void> {
  const select = levelSelect();

  select.value = value;
  select.dispatchEvent(new Event('change', { bubbles: true }));
  await settle();
}

const builtinRow = (key: string): HTMLElement =>
  document.body.querySelector<HTMLElement>(`[data-builtin="${key}"]`)!;
const eventRow = (key: string): HTMLElement =>
  document.body.querySelector<HTMLElement>(`[data-event="${key}"]`)!;
const field = (name: string): HTMLTextAreaElement =>
  document.body.querySelector<HTMLTextAreaElement>(`[data-field="${name}"]`)!;
const switches = (): HTMLElement[] =>
  Array.from(document.body.querySelectorAll<HTMLElement>('[role="dialog"] [role="switch"]'));

const DEBOUNCE_WAIT_MS = 500;

async function typeQuickly(name: string, text: string): Promise<void> {
  const area = field(name);

  area.value = text;
  area.dispatchEvent(new Event('input', { bubbles: true }));
  await settle();
}

async function type(name: string, text: string): Promise<void> {
  await typeQuickly(name, text);
  await new Promise((done) => setTimeout(done, DEBOUNCE_WAIT_MS));
  await settle();
}

async function editRow(target: HTMLElement): Promise<void> {
  target.querySelector<HTMLElement>('[data-edit]')!.click();
  await settle();
}

describe('AdminPlatformDefaultsView', () => {
  it('lists every action with its role name, its source and how many channels set their own', async () => {
    await open();

    expect(document.body.querySelectorAll('[data-action]')).toHaveLength(2);
    expect(row('moderation.ban').textContent).toContain('Moderators');
    expect(row('moderation.ban').textContent).toContain('3 channels use their own');
    expect(row('moderation.ban').querySelector('[data-changed]')).toBeNull();
    expect(row('timers.create').textContent).toContain('Lead moderators');
    expect(row('timers.create').querySelector('[data-changed]')).not.toBeNull();
  });

  it('keeps Apply off until the preview of the chosen level arrived, then sends the counted number', async () => {
    await open();
    await edit('moderation.ban');
    expect(applyButton().disabled).toBe(true);

    await choose('2');

    expect(gets.at(-1)).toMatchObject({
      path: '/api/v{version}/admin/platform-defaults/actions/{actionKey}/blast-radius',
      query: { level: 2 },
    });

    expect(preview()?.textContent).toContain('12 channels');
    expect(preview()?.textContent).toContain('alpha');
    expect(applyButton().disabled).toBe(false);
    expect(applyButton().textContent).toContain('12');

    applyButton().click();
    await settle();

    expect(puts).toHaveLength(1);

    expect(puts[0]).toMatchObject({
      path: '/api/v{version}/admin/platform-defaults/actions/{actionKey}',
      body: {
        level: 2,
        confirmedChannelsAffected: 12,
        confirmDanger: false,
      },
    });
  });

  it('drops the old preview when the level changes, so Apply never confirms a stale count', async () => {
    await open();
    await edit('moderation.ban');
    await choose('2');
    expect(applyButton().disabled).toBe(false);

    affected = 40;
    await choose('4');

    expect(preview()?.textContent).toContain('40 channels');
    expect(preview()?.textContent).not.toContain('12 channels');

    applyButton().click();
    await settle();

    expect(puts[0]?.body).toMatchObject({ level: 4, confirmedChannelsAffected: 40 });
  });

  it('asks for a danger confirmation when the server says so, and sends it', async () => {
    dangerous = true;
    await open();
    await edit('moderation.ban');
    await choose('0');

    expect(document.body.querySelector('[data-confirm-danger]')).not.toBeNull();
    expect(applyButton().disabled).toBe(true);

    document.body.querySelector<HTMLElement>('[data-confirm-danger]')!.click();
    await settle();
    expect(applyButton().disabled).toBe(false);

    applyButton().click();
    await settle();

    expect(puts[0]?.body).toMatchObject({ level: 0, confirmDanger: true });
  });

  it('previews and sends a null level to go back to the shipped default', async () => {
    await open();
    await edit('timers.create');
    await choose('shipped');

    expect(gets.at(-1)?.query?.level).toBeUndefined();

    applyButton().click();
    await settle();

    expect(puts[0]?.body).toMatchObject({ level: null, confirmedChannelsAffected: 12 });
  });

  it('offers no role below the floor the server reported, plus the shipped default', async () => {
    await open();
    await edit('timers.create');

    const values = Array.from(levelSelect().options).map((option) => option.value);

    expect(values).toEqual(['shipped', '6', '10', '20', '30', '40']);
    expect(levelSelect().value).toBe('20');
  });

  it('names an action in plain words and never prints its internal key', async () => {
    await open();

    expect(row('moderation.ban').textContent).toContain('Ban a viewer');
    expect(row('moderation.ban').querySelector('p')?.textContent).not.toContain('moderation.ban');
    expect(row('moderation.ban').querySelector('code')).toBeNull();
  });

  it('keeps the detail line of every action row in one fixed stack', async () => {
    await open();

    const details = document.body.querySelectorAll('[data-action] [data-detail]');

    expect(details).toHaveLength(2);
    details.forEach((detail) => expect(detail.className).toContain('flex-col'));
  });

  it('shows an error, never the empty text, in every section when the server refuses the lists', async () => {
    refused = true;
    await open();

    const sections = ['actions', 'builtin-replies', 'event-responses', 'tts-voice'];

    sections.forEach((name) => {
      const text = document.body.querySelector(`[data-section="${name}"]`)?.textContent ?? '';

      expect(text, name).toContain('You may not see the platform defaults.');
      expect(text, name).not.toMatch(/No .* (are|is) listed/);
    });
  });

  it('shows the forbidden text when the server refuses the list', async () => {
    refused = true;
    await open();

    expect(document.body.textContent).toContain('You may not see the platform defaults.');
  });

  describe('built-in replies', () => {
    it('lists every reply slot with the wording channels follow and how many have their own', async () => {
      await open();

      expect(document.body.querySelectorAll('[data-builtin]')).toHaveLength(2);
      expect(builtinRow('lurk.start').textContent).toContain('Enjoy the lurk, {user}');
      expect(builtinRow('lurk.start').textContent).toContain('2 channels use their own');
      expect(builtinRow('lurk.start').querySelector('[data-changed]')).toBeNull();
      expect(builtinRow('hug.self').textContent).toContain('Hug yourself, {user}');
      expect(builtinRow('hug.self').querySelector('[data-changed]')).not.toBeNull();
    });

    it('keeps Apply off until the typed wording was counted, drops the count on the next keystroke, then sends it', async () => {
      await open();
      await editRow(builtinRow('lurk.start'));
      expect(applyButton().disabled).toBe(true);

      await type('builtin-template', 'See you later, {user}');

      expect(posts.at(-1)).toMatchObject({
        path: '/api/v{version}/admin/platform-defaults/builtin-replies/{builtinKey}/{slot}/blast-radius',
        body: { template: 'See you later, {user}' },
      });

      expect(preview()?.textContent).toContain('12 channels');
      expect(applyButton().disabled).toBe(false);

      affected = 30;
      await typeQuickly('builtin-template', 'See you, {user}');

      expect(preview()).toBeNull();
      expect(applyButton().disabled).toBe(true);

      await new Promise((done) => setTimeout(done, DEBOUNCE_WAIT_MS));
      await settle();
      expect(preview()?.textContent).toContain('30 channels');

      applyButton().click();
      await settle();

      expect(puts).toHaveLength(1);

      expect(puts[0]).toMatchObject({
        path: '/api/v{version}/admin/platform-defaults/builtin-replies/{builtinKey}/{slot}',
        body: { template: 'See you, {user}', confirmedChannelsAffected: 30 },
      });
    });

    it('previews and saves a null template when the shipped wording is chosen', async () => {
      await open();
      await editRow(builtinRow('hug.self'));
      document.body.querySelector<HTMLElement>('[data-use-shipped]')!.click();
      await settle();

      expect(posts.at(-1)?.body).toEqual({ template: null });

      applyButton().click();
      await settle();

      expect(puts[0]?.body).toMatchObject({ template: null, confirmedChannelsAffected: 12 });
    });
  });

  describe('event responses', () => {
    it('lists every event with its state, its text and who follows or has their own', async () => {
      await open();

      expect(document.body.querySelectorAll('[data-event]')).toHaveLength(2);
      expect(eventRow('channel.follow').textContent).toContain('Welcome {user}');
      expect(eventRow('channel.follow').textContent).toContain('9 channels follow');
      expect(eventRow('channel.follow').textContent).toContain('4 channels use their own');
      expect(eventRow('channel.follow').querySelector('[data-enabled]')).not.toBeNull();
      expect(eventRow('channel.raid').querySelector('[data-enabled]')).toBeNull();
    });

    it('counts the draft first, then saves the enabled state, text and speech with the counted number', async () => {
      await open();
      await editRow(eventRow('channel.raid'));
      expect(applyButton().disabled).toBe(true);

      await type('event-message', 'Raid from {user}');

      expect(posts.at(-1)).toMatchObject({
        path: '/api/v{version}/admin/platform-defaults/event-responses/{eventType}/blast-radius',
        body: {
          isEnabled: false,
          message: 'Raid from {user}',
          speakWithTts: true,
        },
      });

      expect(applyButton().disabled).toBe(false);

      affected = 7;
      switches()[0]!.click();
      await settle();
      expect(preview()).toBeNull();
      expect(applyButton().disabled).toBe(true);

      await new Promise((done) => setTimeout(done, DEBOUNCE_WAIT_MS));
      await settle();
      expect(preview()?.textContent).toContain('7 channels');

      applyButton().click();
      await settle();

      expect(puts[0]).toMatchObject({
        path: '/api/v{version}/admin/platform-defaults/event-responses/{eventType}',
        body: {
          isEnabled: true,
          message: 'Raid from {user}',
          speakWithTts: true,
          confirmedChannelsAffected: 7,
        },
      });
    });
  });

  describe('text-to-speech voice', () => {
    const voiceRow = (id: string): HTMLElement =>
      document.body.querySelector<HTMLElement>(`[data-voice-id="${id}"]`)!;

    async function openVoiceEditor(): Promise<void> {
      await open();

      document.body.querySelector<HTMLElement>('[data-section="tts-voice"] [data-edit]')!.click();

      await settle();
    }

    it('shows the current voice with how many channels follow it and how many picked their own', async () => {
      await open();

      const card = document.body.querySelector('[data-section="tts-voice"]')!.textContent;

      expect(card).toContain('Aria');
      expect(card).toContain('7 channels follow this');
      expect(card).toContain('2 channels use their own');
    });

    it('groups the candidate voices by language, each with a play button', async () => {
      await openVoiceEditor();

      const headings = Array.from(document.body.querySelectorAll('[data-voice-group]')).map(
        (heading) => heading.textContent?.trim(),
      );

      expect(headings).toEqual(['English (United States)', 'Dutch (Netherlands)']);
      expect(document.body.querySelectorAll('[data-voice-row]')).toHaveLength(3);

      expect(
        voiceRow('en-US-Guy').parentElement?.querySelector('[data-voice-play]'),
      ).not.toBeNull();
    });

    it('previews the picked voice, keeps Apply off until the count arrived, then sends that count', async () => {
      await openVoiceEditor();
      expect(applyButton().disabled).toBe(true);

      voiceRow('en-US-Guy').click();
      await settle();

      expect(posts.at(-1)).toMatchObject({
        path: '/api/v{version}/admin/platform-defaults/tts-voice/blast-radius',
        body: { voiceId: 'en-US-Guy' },
      });

      expect(preview()?.textContent).toContain('12 channels');
      expect(applyButton().disabled).toBe(false);

      applyButton().click();
      await settle();

      expect(puts[0]).toMatchObject({
        path: '/api/v{version}/admin/platform-defaults/tts-voice',
        body: { voiceId: 'en-US-Guy', confirmedChannelsAffected: 12 },
      });
    });
  });

  it('is an admin-only route', () => {
    expect(dashboardAdminPlatformDefaultsRoute.name).toBe('dashboard-admin-platform-defaults');
    expect(dashboardAdminPlatformDefaultsRoute.beforeEnter).toBeTypeOf('function');
  });
});
