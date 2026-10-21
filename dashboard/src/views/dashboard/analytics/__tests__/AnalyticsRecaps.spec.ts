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

import { onTheWire } from '@/api/__tests__/wire';
import { i18n } from '@/i18n';

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

const recap = (id: string, day: string, title: string, size?: { w: number; h: number }) => ({
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
    duration: 10_800,
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
];

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (_path: string, options?: Options) => {
      queries.push(options?.params?.query);

      return {
        data: {
          data: onTheWire(recaps),
          total: 2,
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

async function open(): Promise<void> {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  mounted = mount(AnalyticsRecaps, {
    attachTo: document.body,
    global: { plugins: [i18n, pinia, [VueQueryPlugin, { queryClient }]] },
  });

  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();
}

const rows = (): string[] =>
  [...document.body.querySelectorAll('[data-recap-row]')].map((row) => row.textContent ?? '');

describe('the Recaps tab', () => {
  it('lists the kept recaps from the endpoint, newest first, with date, title, length and size', async () => {
    await open();

    expect(queries).toEqual([{ Page: 1, PageSize: 12 }]);
    expect(rows()).toHaveLength(2);
    expect(rows()[0]).toContain('Celeste night');
    expect(rows()[0]).toContain('3 hr');
    expect(rows()[0]).toContain('2560 x 1440');
    expect(rows()[1]).toContain('Older stream');
    expect(rows()[1]).toContain('1920 x 1080 (assumed');
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
