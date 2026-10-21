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

import { onTheWire } from '@/api/__tests__/wire';
import { i18n } from '@/i18n';
import { merge } from '@/lib/array';

import AnalyticsRecaps from '../AnalyticsRecaps.vue';
import en from '../i18n/en.json';
import { pngSize } from '../recapExport';

type Options = { params?: { path?: Record<string, unknown>; query?: Record<string, unknown> } };
type CaptureOptions = {
  width: number;
  height: number;
  pixelRatio: number;
};

const queries: Array<Record<string, unknown> | undefined> = [];

// Recaps a test adds after the three standard ones.
const extra: Record<string, unknown>[] = [];

const recap = (
  id: string,
  day: string,
  title: string,
  size?: { w: number; h: number },
  duration = 10_800,
) => ({
  streamId: id,
  fromAt: `${day}T18:00:00Z`,
  toAt: `${day}T21:00:00Z`,
  updatedAt: `${day}T21:00:00Z`,
  outputWidth: size?.w,
  outputHeight: size?.h,
  stats: {
    stream: {
      id,
      startedAt: `${day}T18:00:00Z`,
      endedAt: `${day}T21:00:00Z`,
      title,
      game: 'Celeste',
    },
    channel: { name: 'Stoney_Eagle', avatarUrl: 'https://cdn.example/stoney.png' },
    duration,
    peakViewers: 87,
    followers: 4,
    chat: { messages: 900, uniqueChatters: 31 },
  },
}) as const satisfies Record<string, unknown>;

function withoutChannel(kept: ReturnType<typeof recap>) {
  const { channel: _channel, ...stats } = kept.stats;

  return { ...kept, stats };
}

// Newest first, as the server sends them.
const recaps = [
  recap('s2', '2026-10-18', 'Celeste night', { w: 2560, h: 1440 }),
  // A recap kept before the channel metric existed stores no channel.
  withoutChannel(recap('s1', '2026-10-10', 'Older stream')),
  // The 24 hour stream: the longest and the oldest.
  recap('s0', '2026-10-02', 'Longest', undefined, 86_400),
];

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (_path: string, options?: Options) => {
      queries.push(options?.params?.query);

      return {
        data: {
          data: onTheWire(merge(recaps, extra)),
          total: 3 + extra.length,
          hasMore: false,
        },
        response: { ok: true, status: 200 },
      };
    },
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({
    channelId: ref('c1'),
    current: ref({
      login: 'stoney_eagle',
      displayName: 'Stoney_Eagle',
      profileImageUrl: 'https://cdn.example/current.png',
    }),
  }),
}));

// A PNG whose header states the size the capture was asked for.
const toPng = vi.hoisted(() => vi.fn());

vi.mock('html-to-image', () => ({ toPng }));

function pngOf(width: number, height: number): string {
  const bytes = new Uint8Array(24);
  const view = new DataView(bytes.buffer);

  bytes.set([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a], 0);
  bytes.set([0x49, 0x48, 0x44, 0x52], 12);
  view.setUint32(16, width);
  view.setUint32(20, height);

  return `data:image/png;base64,${btoa(String.fromCharCode(...bytes))}`;
}

let mounted: VueWrapper | null = null;
let pinia: Pinia;
const saved: string[] = [];

beforeEach(() => {
  queries.length = 0;
  extra.length = 0;
  saved.length = 0;
  pinia = createPinia();
  setActivePinia(pinia);
  i18n.global.mergeLocaleMessage('en', en);

  vi.stubGlobal('ResizeObserver', class {
    observe(): void {}
    unobserve(): void {}
    disconnect(): void {}
  });

  toPng.mockImplementation(async (_node: HTMLElement, options: CaptureOptions) =>
    pngOf(options.width * options.pixelRatio, options.height * options.pixelRatio));

  vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
    saved.push(this.href);
  });
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
  toPng.mockReset();
});

let router: Router;

async function open(url = '/'): Promise<void> {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/', component: { render: () => null } }] });
  await router.push(url);

  mounted = mount(AnalyticsRecaps, {
    attachTo: document.body,
    global: { plugins: [i18n, pinia, router, [VueQueryPlugin, { queryClient }]] },
  });

  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();
}

const titles = (): string[] =>
  [...document.body.querySelectorAll('[data-recap-row] td:nth-child(3)')].map((cell) => cell.textContent ?? '');

const headerSort = (key: string): string | null =>
  document.body.querySelector(`[data-sort="${key}"]`)!.closest('th')!.getAttribute('aria-sort');

const rows = (): string[] =>
  [...document.body.querySelectorAll('[data-recap-row]')].map((row) => row.textContent ?? '');

describe('the Recaps tab', () => {
  it('lists the kept recaps from the endpoint, newest first, with date, title and length, and no saved size', async () => {
    await open();

    expect(queries).toEqual([{ Page: 1, PageSize: 100 }]);
    expect(rows()).toHaveLength(3);
    expect(rows()[0]).toContain('Celeste night');
    expect(rows()[0]).toContain('3 hr');
    expect(rows()[1]).toContain('Older stream');
    expect(rows()[2]).toContain('Longest');
    expect(rows().join(' ')).not.toMatch(/\d+ x \d+/);
    expect(document.body.querySelector('[data-sort="size"]')).toBeNull();
  });

  it('counts in its header exactly the recaps it lists', async () => {
    extra.push(recap('s-extra', '2026-10-05', 'Fourth'));
    await open();

    expect(rows()).toHaveLength(4);
    expect(document.body.textContent).toContain('4 recaps kept');
  });

  it('shows a dash, never a zero or an empty cell, for a title and a peak that were never stored', async () => {
    const unknown = recap('s-unknown', '2026-10-05', '');

    extra.push({
      ...unknown,
      stats: {
        ...unknown.stats,
        stream: { ...unknown.stats.stream, title: null },
        peakViewers: null,
      },
    });

    await open();

    const cells = [...document.body.querySelectorAll('[data-recap-row]')]
      .map((row) => [...row.querySelectorAll('td')].map((cell) => cell.textContent?.trim()))
      .find((row) => row.includes('–'));

    expect(cells?.[2]).toBe('–');
    expect(cells?.[4]).toBe('–');
    expect(cells?.[5]).toBe('900');
  });

  it('sorts a recap with an unknown peak below every known peak', async () => {
    const unknown = recap('s-unknown', '2026-10-05', 'No samples');

    extra.push({ ...unknown, stats: { ...unknown.stats, peakViewers: null } });
    await open('/?sort=peak&dir=desc');

    expect(titles().at(-1)).toBe('No samples');
  });

  it('shows the peak and the chat lines of each recap', async () => {
    await open();

    expect(rows()[0]).toContain('87');
    expect(rows()[0]).toContain('900');
  });

  it('sorts by length, longest first, and keeps the sort in the URL', async () => {
    await open();
    document.body.querySelector<HTMLButtonElement>('[data-sort="length"]')!.click();
    await flushPromises();

    expect(titles()).toEqual(['Longest', 'Celeste night', 'Older stream']);
    expect(router.currentRoute.value.query).toMatchObject({ sort: 'length', dir: 'desc' });

    document.body.querySelector<HTMLButtonElement>('[data-sort="length"]')!.click();
    await flushPromises();

    expect(titles()[0]).not.toBe('Longest');
    expect(titles()[2]).toBe('Longest');
    expect(router.currentRoute.value.query).toMatchObject({ sort: 'length', dir: 'asc' });
  });

  it('sorts by title from A to Z on the first press, and reads the sort from the URL', async () => {
    await open('/?sort=title&dir=asc');

    expect(titles()).toEqual(['Celeste night', 'Longest', 'Older stream']);
    expect(headerSort('title')).toBe('ascending');
    expect(headerSort('started')).toBe('none');
  });

  it('sorts by date when asked and opens newest first by default', async () => {
    await open();

    expect(headerSort('started')).toBe('descending');

    document.body.querySelector<HTMLButtonElement>('[data-sort="started"]')!.click();
    await flushPromises();

    expect(titles()).toEqual(['Longest', 'Older stream', 'Celeste night']);
  });

  it('gives every sortable column a button and fixes the table layout and the column widths', async () => {
    await open();

    const keys = ['started', 'title', 'length', 'peak', 'chat'];
    const buttons = [...document.body.querySelectorAll('[data-sort]')].map((button) => button.getAttribute('data-sort'));

    expect(buttons).toEqual(keys);
    expect(document.body.querySelector('table[data-slot="table"]')!.className).toContain('table-fixed');

    for (const key of keys.filter((known) => known !== 'title')) {
      const head = document.body.querySelector(`[data-sort="${key}"]`)!.closest('th')!;

      expect(head.className, key).toMatch(/\bw-\d+\b/);
    }

    const cell = document.body.querySelector('[data-recap-row] td:nth-child(3)')!;

    expect(cell.className).toContain('truncate');
    expect(cell.getAttribute('title')).toBe('Celeste night');
  });

  it('opens a recap drawn by the real raid_recap widget with that recap\'s data', async () => {
    await open();
    document.body.querySelector<HTMLButtonElement>('[data-recap-open]')!.click();
    await flushPromises();
    await new Promise((done) => setTimeout(done, 0));
    await flushPromises();

    const frame = document.body.querySelector<HTMLElement>('[data-recap-frame]')!;

    expect(frame.style.width).toBe('2560px');
    expect(frame.style.height).toBe('1440px');
    expect(frame.querySelector('.recap')).not.toBeNull();
    expect(frame.textContent).toContain('Peak viewers');
    expect(frame.textContent).toContain('87');
    expect(frame.querySelector('[data-block="streamer"]')).toBeNull();

    expect(frame.querySelector('.grid > .block img.watermark')?.getAttribute('src')).toBe(
      'https://cdn.example/stoney.png',
    );
  });

  it('draws the watermark of a recap that stored no channel from the managed channel', async () => {
    await open();
    const thumbnails = [...document.body.querySelectorAll('[data-recap-row]')].map((row) => row.textContent);

    expect(thumbnails[1]).toContain('S');
    document.body.querySelectorAll<HTMLButtonElement>('[data-recap-open]')[1]!.click();
    await flushPromises();
    await new Promise((done) => setTimeout(done, 0));
    await flushPromises();

    const frame = document.body.querySelector('[data-recap-frame]')!;

    expect(frame.querySelector('[data-block="streamer"]')).toBeNull();
    expect(frame.querySelector('img.watermark')?.getAttribute('src')).toBe('https://cdn.example/current.png');
    expect(frame.querySelector('.sub')?.textContent).toContain('Stoney_Eagle');
  });

  it('saves the PNG at the stored resolution, and 1920 x 1080 for an old recap', async () => {
    await open();
    const openButtons = document.body.querySelectorAll<HTMLButtonElement>('[data-recap-open]');

    openButtons[0]!.click();
    await flushPromises();
    await new Promise((done) => setTimeout(done, 0));
    document.body.querySelector<HTMLButtonElement>('[data-recap-save]')!.click();
    await flushPromises();

    expect(pngSize(saved[0]!)).toEqual({ width: 2560, height: 1440 });

    expect(toPng.mock.calls[0]![1]).toMatchObject({
      width: 2560,
      height: 1440,
      pixelRatio: 1,
    });

    document.body.querySelector<HTMLButtonElement>('[role="dialog"] button[type="button"]')?.click();
    mounted!.unmount();
    document.body.innerHTML = '';
    await open();
    document.body.querySelectorAll<HTMLButtonElement>('[data-recap-open]')[1]!.click();
    await flushPromises();
    await new Promise((done) => setTimeout(done, 0));
    document.body.querySelector<HTMLButtonElement>('[data-recap-save]')!.click();
    await flushPromises();

    expect(pngSize(saved[1]!)).toEqual({ width: 1920, height: 1080 });
  });
});
