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

import { i18n } from '@/i18n';

import OverlaysView from '../OverlaysView.vue';
import en from '../i18n/en.json';

type Options = {
  params?: { path?: Record<string, unknown>; query?: Record<string, unknown> };
  body?: unknown;
};
type Call = {
  method: string;
  path: string;
  body: unknown;
};
type Answer = {
  data?: unknown;
  error?: unknown;
  response: { status: number; ok: boolean };
};
type Field = Record<string, unknown>;

const calls: Call[] = [];
let widgets: Record<string, unknown>[] = [];
let schemas: Record<string, Field[]> = {};
let putRefusal: string | null = null;
let testRefusal: string | null = null;

const text = (value: string): { key: string; text: string } => ({ key: `t.${value}`, text: value });

const fields = (): Field[] => [
  {
    key: 'showArt',
    label: text('Show album art'),
    type: 'bool',
    group: text('Appearance'),
    default: true,
  },
  {
    key: 'size',
    label: text('Font size'),
    type: 'number',
    group: text('Appearance'),
    default: 24,
    min: 10,
    max: 72,
    step: 2,
  },
  {
    key: 'accent',
    label: text('Accent color'),
    type: 'color',
    group: text('Appearance'),
    default: '#ff0000',
  },
  {
    key: 'title',
    label: text('Title'),
    type: 'text',
    group: text('Content'),
    default: 'Now playing',
    help: text('Shown above the song'),
  },
  {
    key: 'position',
    label: text('Position'),
    type: 'select',
    group: text('Content'),
    default: 'left',
    options: [
      { value: 'left', label: text('Left') },
      { value: 'right', label: text('Right') },
    ],
  },
  {
    key: 'parts',
    label: text('Parts'),
    type: 'multiselect',
    group: text('Content'),
    default: ['title'],
    options: [
      { value: 'title', label: text('Song title') },
      { value: 'artist', label: text('Artist') },
    ],
  },
  {
    key: 'colors',
    label: text('Colors'),
    type: 'json',
    group: text('Data'),
    default: {},
  },
];

const widget = (
  id: string,
  name: string,
  over: Record<string, unknown> = {},
): Record<string, unknown> => ({
  id,
  name,
  description: null,
  framework: 'vue',
  source: 'user',
  isEnabled: true,
  overlayUrl: null,
  activeVersionId: null,
  galleryItemId: null,
  settings: {},
  eventSubscriptions: [],
  lastRuntimeError: null,
  lastRanAt: null,
  createdAt: '2026-01-01T10:00:00Z',
  updatedAt: '2026-01-01T10:00:00Z',
  galleryUpdateAvailable: false,
  isAttached: false,
  isCustomized: false,
  ...over,
});

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string, options?: Options): Promise<Answer> => {
      calls.push({
        method: 'GET',
        path,
        body: undefined,
      });

      if (path.endsWith('/settings-schema')) {
        const id = String(options?.params?.path?.widgetId);

        return {
          data: {
            data: {
              widgetKey: id,
              name: id,
              fields: schemas[id] ?? [],
              eventSubscriptions: [],
            },
          },
          response: { status: 200, ok: true },
        };
      }

      return {
        data: {
          data: widgets,
          hasMore: false,
          nextPage: null,
        },
        response: { status: 200, ok: true },
      };
    },
    PUT: async (path: string, options?: Options): Promise<Answer> => {
      calls.push({
        method: 'PUT',
        path,
        body: options?.body,
      });

      if (putRefusal !== null)
        return { error: { message: putRefusal }, response: { status: 400, ok: false } };

      const id = String(options?.params?.path?.widgetId);
      const body = options?.body as Record<string, unknown>;

      widgets = widgets.map((row) =>
        row.id === id
          ? { ...row, ...body }
          : row);

      return { data: { data: null }, response: { status: 200, ok: true } };
    },
    POST: async (path: string, options?: Options): Promise<Answer> => {
      calls.push({
        method: 'POST',
        path,
        body: options?.body,
      });

      if (testRefusal !== null)
        return { error: { message: testRefusal }, response: { status: 400, ok: false } };

      return { data: { data: 'Sent to 1 browser source.' }, response: { status: 200, ok: true } };
    },
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;
let pinia: Pinia;
let router: Router;

beforeEach(() => {
  calls.length = 0;
  putRefusal = null;
  testRefusal = null;

  widgets = [
    widget('w1', 'Now Playing', {
      settings: { size: 30, extra: 'kept' },
      eventSubscriptions: ['follow', 'channel.raid'],
    }),
    widget('w2', 'Raw', { settings: { speed: 5 } }),
    widget('w3', 'Bare', { settings: {} }),
    widget('w4', 'Sized', { settings: { width: 1920, height: 1080 } }),
  ];

  schemas = { w1: fields() };
  pinia = createPinia();
  setActivePinia(pinia);
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

const settle = async (): Promise<void> => {
  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();
};

async function open(url: string = '/app/overlays?id=w1'): Promise<void> {
  const blank = { template: '<div />' };

  router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/app/overlays',
        name: 'dashboard-overlays',
        component: blank,
      },
    ],
  });

  await router.push(url);

  mounted = mount(OverlaysView, {
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

  await settle();
}

const settings = (): HTMLElement =>
  document.body.querySelector('[data-slot="overlay-settings"]') as HTMLElement;
const row = (key: string): HTMLElement =>
  settings().querySelector(`[data-setting="${key}"]`) as HTMLElement;
const input = (key: string): HTMLInputElement =>
  row(key).querySelector('input') as HTMLInputElement;
const saveButton = (): HTMLButtonElement =>
  settings().querySelector('[data-save]') as HTMLButtonElement;
const changed = (key: string): boolean => row(key).dataset.changed === 'true';
const puts = (): Call[] => calls.filter((call) => call.method === 'PUT');

async function type(key: string, value: string): Promise<void> {
  const field = input(key);

  field.value = value;
  field.dispatchEvent(new Event('input', { bubbles: true }));
  field.dispatchEvent(new Event('change', { bubbles: true }));
  await settle();
}

describe('the overlay settings form', () => {
  it('renders the control each field type asks for', async () => {
    await open();

    expect(row('showArt').querySelector('[role="switch"]')).not.toBeNull();
    const size = input('size');

    expect(size.type).toBe('number');
    expect([size.min, size.max, size.step]).toEqual(['10', '72', '2']);
    expect(size.value).toBe('30');
    expect(row('accent').querySelector('[data-swatch]')).not.toBeNull();
    expect(input('accent').value).toBe('#ff0000');
    expect(input('title').value).toBe('Now playing');
    expect(row('title').textContent).toContain('Shown above the song');
    expect(row('position').querySelector('[role="combobox"]')?.textContent).toContain('Left');
    const parts: HTMLButtonElement[] = [
      ...row('parts').querySelectorAll<HTMLButtonElement>('button'),
    ];

    expect(parts.map((b) => [b.textContent?.trim(), b.dataset.state])).toEqual([
      ['Song title', 'on'],
      ['Artist', 'off'],
    ]);

    const json = row('colors').querySelector('textarea') as HTMLTextAreaElement;

    expect(json.readOnly).toBe(true);
    expect(JSON.parse(json.value)).toEqual({});
    expect(settings().textContent).toContain('Appearance');
    expect(settings().textContent).toContain('Content');
  });

  it('holds a number inside its min and max', async () => {
    await open();

    await type('size', '100');
    expect(input('size').value).toBe('72');

    await type('size', '2');
    expect(input('size').value).toBe('10');
  });

  it('Reset puts the default back, clears the changed mark and leaves the form to save', async () => {
    await open();

    expect(changed('size')).toBe(true);

    expect(saveButton().disabled).toBe(true);

    (row('size').querySelector('[data-reset]') as HTMLButtonElement).click();
    await settle();

    expect(input('size').value).toBe('24');
    expect(changed('size')).toBe(false);
    expect(row('size').querySelector('[data-reset]')).toBeNull();
    expect(saveButton().disabled).toBe(false);
  });

  it('Save sends the saved map with only the changed value merged over it', async () => {
    await open();
    expect(saveButton().disabled).toBe(true);

    await type('title', 'Hello');
    expect(saveButton().disabled).toBe(false);
    saveButton().click();
    await settle();

    expect(puts().map((call) => call.body)).toEqual([
      {
        settings: {
          size: 30,
          extra: 'kept',
          title: 'Hello',
        },
      },
    ]);

    expect(input('title').value).toBe('Hello');
    expect(saveButton().disabled).toBe(true);
    expect(settings().textContent).toContain(en.dashboard.overlays.detail.settings.saved);
  });

  it('a refused Save keeps the change, keeps Save on and says it was not saved', async () => {
    putRefusal = 'Settings refused';
    await open();

    await type('title', 'Hello');
    saveButton().click();
    await settle();

    expect(puts()).toHaveLength(1);
    expect(input('title').value).toBe('Hello');
    expect(saveButton().disabled).toBe(false);
    expect(settings().textContent).toContain(en.dashboard.overlays.detail.settings.failed);
  });

  it('without a schema shows the saved keys read-only, or says there are none', async () => {
    await open('/app/overlays?id=w2');

    expect(settings().textContent).toContain('speed');
    expect(settings().textContent).toContain('5');
    expect(settings().querySelector('input')).toBeNull();
    expect(saveButton()).toBeNull();

    mounted?.unmount();
    await open('/app/overlays?id=w3');
    expect(settings().textContent).toContain(en.dashboard.overlays.detail.settings.none);
  });
});

describe('the Test button on a trigger row', () => {
  const testButton = (event: string): HTMLButtonElement =>
    document.body.querySelector(`[data-event="${event}"] [data-test]`) as HTMLButtonElement;
  const eventRow = (event: string): HTMLElement =>
    document.body.querySelector(`[data-event="${event}"]`) as HTMLElement;

  it('sends the event type without data and shows what the bot answered', async () => {
    await open();

    testButton('follow').click();
    await settle();

    expect(calls.filter((call) => call.method === 'POST')).toEqual([
      {
        method: 'POST',
        path: '/api/v{version}/channels/{channelId}/widgets/test-event',
        body: { eventType: 'follow' },
      },
    ]);

    expect(eventRow('follow').textContent).toContain('Sent to 1 browser source.');
  });

  it('shows the reason when the bot refuses the test', async () => {
    testRefusal = 'Unknown event type.';
    await open();

    testButton('channel.raid').click();
    await settle();

    expect(eventRow('channel.raid').textContent).toContain('Unknown event type.');
    expect(eventRow('channel.raid').textContent).not.toContain('Sent to 1 browser source.');
  });
});

describe('the preview', () => {
  it('says why there is no preview and shows the size the overlay is set to', async () => {
    await open('/app/overlays?id=w4');

    const preview = document.body.querySelector('[data-slot="overlay-preview"]') as HTMLElement;

    expect(preview.textContent).toContain(en.dashboard.overlays.detail.preview.unavailable);
    expect(preview.textContent).toContain('1920 × 1080');
  });
});
