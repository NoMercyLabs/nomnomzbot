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
import { ref } from 'vue';

import { i18n } from '@/i18n';
import common from '@/i18n/locales/en.json';

import SoundsView from '../SoundsView.vue';
import en from '../i18n/en.json';

type ClipRow = {
  id: string;
  name: string;
  displayName: string;
  mimeType: string;
  durationMs: number;
  sizeBytes: number;
  defaultVolume: number;
  isEnabled: boolean;
  cooldownSeconds: number;
  minPermissionLevel: string;
  triggerWord: string | null;
  createdAt: string;
  previewUrl: string;
};

const clip = (id: string, displayName: string): ClipRow => ({
  id,
  name: id,
  displayName,
  mimeType: 'audio/mpeg',
  durationMs: 2500,
  sizeBytes: 1000,
  defaultVolume: 80,
  isEnabled: true,
  cooldownSeconds: 0,
  minPermissionLevel: 'Everyone',
  triggerWord: null,
  createdAt: '2026-10-01T12:00:00Z',
  previewUrl: '',
});

type Init = {
  body?: Record<string, unknown>;
  params?: { path: { id: string } };
  bodySerializer?: (body: unknown) => unknown;
};

let clips: ClipRow[] = [];
let blastCount = 0;
const calls: string[] = [];
const bodies: Record<string, unknown>[] = [];
let uploaded: FormData | null = null;

const ok = { status: 200, ok: true };

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string) => {
      calls.push(`GET ${path}`);

      if (path.endsWith('/blast-radius')) {
        return {
          data: {
            data: {
              isMinimum: false,
              categories: blastCount === 0
                ? []
                : [
                    {
                      categoryKey: 'blast_radius_category_commands',
                      count: blastCount,
                      sample: [],
                    },
                  ],
            },
          },
          response: ok,
        };
      }

      return { data: {
        data: clips,
        total: clips.length,
        hasMore: false,
      }, response: ok };
    },
    POST: async (path: string, init: Init) => {
      calls.push(`POST ${path}`);
      uploaded = init.bodySerializer?.(init.body) as FormData;
      const shownName = String(uploaded.get('DisplayName'));

      clips = [...clips, clip('new', shownName)];

      return { data: {}, response: ok };
    },
    PUT: async (path: string, init: Init) => {
      calls.push(`PUT ${path} ${init.params?.path.id}`);
      bodies.push(init.body ?? {});

      clips = clips.map((row) =>
        row.id === init.params?.path.id
          ? { ...row, ...init.body } as ClipRow
          : row);

      return { data: {}, response: ok };
    },
    DELETE: async (path: string, init: Init) => {
      calls.push(`DELETE ${path} ${init.params?.path.id}`);
      clips = clips.filter((row) => row.id !== init.params?.path.id);

      return { data: {}, response: ok };
    },
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

const held = vi.hoisted(() => ({ keys: null as Set<string> | null }));

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({
    known: ref(true),
    can: (key: string) => held.keys === null || held.keys.has(key),
  }),
}));

let mounted: VueWrapper | null = null;

beforeEach(() => {
  localStorage.clear();
  held.keys = null;
  calls.length = 0;
  bodies.length = 0;
  uploaded = null;
  blastCount = 0;
  setActivePinia(createPinia());
  i18n.global.mergeLocaleMessage('en', common);
  i18n.global.mergeLocaleMessage('en', en);

  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe(): void {}
      unobserve(): void {}
      disconnect(): void {}
    },
  );
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

async function settle(): Promise<void> {
  await flushPromises();
  await new Promise((done) => setTimeout(done, 400));
  await flushPromises();
}

async function open(): Promise<void> {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  mounted = mount(SoundsView, {
    attachTo: document.body,
    global: { plugins: [i18n, [VueQueryPlugin, { queryClient }]] },
  });

  await settle();
}

async function fill(selector: string, value: string): Promise<void> {
  const input = document.querySelector<HTMLInputElement>(selector);

  if (!input)
    throw new Error(`missing ${selector}`);

  input.value = value;
  input.dispatchEvent(new Event('input', { bubbles: true }));
  await flushPromises();
}

async function chooseFile(file: File): Promise<void> {
  const input = document.querySelector<HTMLInputElement>('[data-sound-file]')!;

  Object.defineProperty(input, 'files', { value: [file], configurable: true });
  input.dispatchEvent(new Event('change', { bubbles: true }));
  await flushPromises();
}

function submitDialog(): void {
  document
    .querySelector('form')
    ?.dispatchEvent(new Event('submit', { cancelable: true, bubbles: true }));
}

const rows = (): string[] =>
  Array.from(document.querySelectorAll('[data-sound-row]')).map((row) => row.textContent ?? '');

describe('SoundsView', () => {
  it('lists each clip with its short name, volume and length', async () => {
    clips = [clip('boom', 'Boom')];
    await open();

    expect(rows()).toHaveLength(1);
    expect(rows()[0]).toContain('Boom');
    expect(rows()[0]).toContain('boom');
    expect(rows()[0]).toContain('volume 80');
    expect(rows()[0]).toContain('2.5 s');
  });

  it('uploads the chosen file as multipart form data with the typed settings', async () => {
    clips = [];
    await open();

    document.querySelector<HTMLElement>('[data-sound-new]')?.click();
    await settle();
    await chooseFile(new File(['abc'], 'boom.mp3', { type: 'audio/mpeg' }));
    await fill('[data-sound-name]', ' boom ');
    await fill('[data-sound-display-name]', ' Boom ');
    await fill('[data-sound-volume]', '60');
    submitDialog();
    await settle();

    expect(calls).toContain('POST /api/v{version}/sound-clips');
    expect(uploaded).toBeInstanceOf(FormData);
    expect(uploaded?.get('Name')).toBe('boom');
    expect(uploaded?.get('DisplayName')).toBe('Boom');
    expect(uploaded?.get('DefaultVolume')).toBe('60');
    expect((uploaded?.get('File') as File).name).toBe('boom.mp3');
    expect(rows()).toHaveLength(1);
  });

  it('refuses an upload without a file and says why, without sending', async () => {
    clips = [];
    await open();

    document.querySelector<HTMLElement>('[data-sound-new]')?.click();
    await settle();
    submitDialog();
    await settle();

    expect(calls.filter((call) => call.startsWith('POST'))).toEqual([]);
    expect(document.body.textContent).toContain(en.dashboard.sounds.dialog.problem.fileRequired);
  });

  it('edits the settings of a clip by its id and keeps it switched on', async () => {
    clips = [clip('boom', 'Boom')];
    await open();

    document.querySelector<HTMLElement>('[data-sound-edit]')?.click();
    await settle();
    await fill('[data-sound-display-name]', 'Big boom');
    await fill('[data-sound-cooldown]', '0:30');
    await fill('[data-sound-trigger]', ' boom ');
    submitDialog();
    await settle();

    expect(calls).toContain('PUT /api/v{version}/sound-clips/{id} boom');

    expect(bodies[0]).toEqual({
      displayName: 'Big boom',
      defaultVolume: 80,
      isEnabled: true,
      cooldownSeconds: 30,
      minPermissionLevel: 'Everyone',
      triggerWord: 'boom',
    });

    expect(rows()[0]).toContain('Big boom');
    expect(rows()[0]).toContain('chat word boom');
  });

  it('switches a clip off with the row switch and sends the other settings unchanged', async () => {
    clips = [clip('boom', 'Boom')];
    await open();

    document.querySelector<HTMLElement>('[data-sound-enabled]')?.click();
    await settle();

    expect(calls).toContain('PUT /api/v{version}/sound-clips/{id} boom');

    expect(bodies[0]).toMatchObject({
      isEnabled: false,
      displayName: 'Boom',
      defaultVolume: 80,
    });

    expect(clips[0]?.isEnabled).toBe(false);
  });

  it('refuses a chat word with a space, without saving', async () => {
    clips = [clip('boom', 'Boom')];
    await open();

    document.querySelector<HTMLElement>('[data-sound-edit]')?.click();
    await settle();
    await fill('[data-sound-trigger]', 'two words');
    submitDialog();
    await settle();

    expect(calls.filter((call) => call.startsWith('PUT'))).toEqual([]);

    expect(document.body.textContent).toContain(
      en.dashboard.sounds.dialog.problem.triggerHasSpace,
    );
  });

  it('shows the counted places that use the clip before the delete, then removes it', async () => {
    clips = [clip('boom', 'Boom')];
    blastCount = 2;
    await open();

    document.querySelector<HTMLElement>('[data-sound-delete]')?.click();
    await settle();

    expect(calls).toContain('GET /api/v{version}/sound-clips/{id}/blast-radius');
    expect(calls.filter((call) => call.startsWith('DELETE'))).toEqual([]);
    expect(document.querySelector('[data-slot="blast-list"]')?.textContent).toContain('2 commands');

    document.querySelector<HTMLElement>('[data-confirm-action]')?.click();
    await settle();

    expect(calls).toContain('DELETE /api/v{version}/sound-clips/{id} boom');
    expect(rows()).toHaveLength(0);
  });

  it('hides add, edit and delete from a person who may only read', async () => {
    held.keys = new Set(['sounds:read']);
    clips = [clip('boom', 'Boom')];
    await open();

    expect(rows()).toHaveLength(1);
    expect(document.querySelector('[data-sound-new]')).toBeNull();
    expect(document.querySelector('[data-sound-edit]')).toBeNull();
    expect(document.querySelector('[data-sound-delete]')).toBeNull();
  });
});
