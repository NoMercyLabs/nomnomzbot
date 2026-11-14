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

// Stoney, 2026-11-10: "we need analytics and all those features we have from twitch to match". A Twitch clip
// goes to the streamer's own YouTube channel from the Clips tab of the Stream page.
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ref } from 'vue';

import { laptopWidth, phoneWidth, setWidth } from '@/__tests__/viewport';
import { i18n } from '@/i18n';

import ClipsPanel from '../ClipsPanel.vue';
import en from '../i18n/en.json';

type Options = {
  params?: { path?: Record<string, unknown>; query?: Record<string, unknown> };
  body?: unknown;
};
type Call = {
  method: string;
  path: string;
  options?: Options;
};
type Answer = {
  data?: unknown;
  error?: unknown;
  response: { status: number; ok: boolean };
};

const calls: Call[] = [];
let listAnswer: (cursor: unknown) => Answer;
let uploadAnswer: () => Answer;

const ok = (data: unknown): Answer => ({ data: { data }, response: { status: 200, ok: true } });
const clip = (id: string, title: string) => ({
  id,
  title,
  url: `https://clips.twitch.tv/${id}`,
  thumbnailUrl: `https://clips.example/${id}.jpg`,
  creatorName: 'SampleViewer',
  createdAt: '2026-11-09T18:00:00Z',
  viewCount: 1234,
  durationSeconds: 28.5,
});
const list = (over: Record<string, unknown> = {}) => ({
  clips: [clip('c1', 'The funny moment'), clip('c2', 'The big win')],
  nextCursor: null,
  uploadsLeftToday: 97,
  ...over,
});

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string, options?: Options): Promise<Answer> => {
      calls.push({
        method: 'GET',
        path,
        options,
      });

      return listAnswer(options?.params?.query?.cursor);
    },
    POST: async (path: string, options?: Options): Promise<Answer> => {
      calls.push({
        method: 'POST',
        path,
        options,
      });

      return uploadAnswer();
    },
  },
}));

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({ known: ref(true), can: (): boolean => true }),
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;

beforeEach(() => {
  calls.length = 0;
  listAnswer = () => ok(list());

  uploadAnswer = () =>
    ok({
      outcome: 'uploaded',
      videoId: 'yt-video-1',
      url: 'https://www.youtube.com/watch?v=yt-video-1',
      uploadsLeftToday: 96,
    });

  setActivePinia(createPinia());
  i18n.global.mergeLocaleMessage('en', en);
});

const realMatchMedia = window.matchMedia;

afterEach(() => {
  window.matchMedia = realMatchMedia;
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

async function open(): Promise<void> {
  mounted = mount(ClipsPanel, {
    attachTo: document.body,
    global: {
      plugins: [
        i18n,
        [
          VueQueryPlugin,
          { queryClient: new QueryClient({ defaultOptions: { queries: { retry: false } } }) },
        ],
      ],
    },
  });

  await flushPromises();
}

const text = (): string => document.body.textContent ?? '';
const rows = (): HTMLElement[] => [...document.body.querySelectorAll<HTMLElement>('[data-clip]')];
const uploadButtons = (): HTMLButtonElement[] => [
  ...document.body.querySelectorAll<HTMLButtonElement>('[data-clip-upload]'),
];
const posts = (): Call[] => calls.filter((call) => call.method === 'POST');
const dialog = (): HTMLElement | null =>
  document.body.querySelector<HTMLElement>('[data-clip-upload-dialog]');

async function openDialog(index: number): Promise<void> {
  uploadButtons()[index]!.click();
  await flushPromises();
}

async function submit(): Promise<void> {
  const primary = [...dialog()!.querySelectorAll<HTMLButtonElement>('button')].find(
    (button) => button.textContent?.trim() === 'Upload',
  );

  primary!.click();
  await flushPromises();
}

describe('ClipsPanel', () => {
  it('on a phone gives the title the room: the upload is a 36px icon button named by its aria-label', async () => {
    setWidth(phoneWidth);
    await open();

    const upload = uploadButtons()[0]!;

    expect(upload.getAttribute('data-size')).toBe('icon');
    expect(upload.getAttribute('aria-label')).toBe('Upload to YouTube');
    expect(upload.textContent?.trim()).toBe('');
    expect(upload.querySelector('svg')!.getAttribute('class')).toContain('size-5');
    expect(rows()[0]!.className).not.toContain('flex-wrap');
  });

  it('on a desktop keeps the written Upload to YouTube button', async () => {
    setWidth(laptopWidth);
    await open();

    const upload = uploadButtons()[0]!;

    expect(upload.getAttribute('data-variant')).toBe('outline');
    expect(upload.textContent?.trim()).toBe('Upload to YouTube');
  });

  it('lists each clip with who made it, when and its views, and says how many uploads are left', async () => {
    await open();

    expect(rows()).toHaveLength(2);
    expect(rows()[0]!.textContent).toContain('The funny moment');
    expect(rows()[0]!.textContent).toContain('By SampleViewer');
    expect(rows()[0]!.textContent).toContain('Nov 9, 2026');
    expect(rows()[0]!.textContent).toContain('1,234 views');
    expect(text()).toContain('97 YouTube uploads left today');
  });

  it('sends the clip with its Twitch title, private, and then shows the link to the video', async () => {
    await open();
    await openDialog(1);

    const title = dialog()!.querySelector<HTMLInputElement>('#clip-upload-title')!;

    expect(title.value).toBe('The big win');

    await submit();

    expect(posts()).toHaveLength(1);
    expect(posts()[0]!.path).toBe('/api/v{version}/channels/{channelId}/clips/{clipId}/youtube');
    expect(posts()[0]!.options?.params?.path).toMatchObject({ channelId: 'c1', clipId: 'c2' });

    expect(posts()[0]!.options?.body).toEqual({
      title: 'The big win',
      description: '',
      privacy: 'private',
    });

    expect(dialog()).toBeNull();

    const done = document.body.querySelector('[data-clip-uploaded]')!;

    expect(done.textContent).toContain('The clip is on your YouTube channel.');

    expect(done.querySelector('a')?.getAttribute('href')).toBe(
      'https://www.youtube.com/watch?v=yt-video-1',
    );
  });

  it('sends the title the person typed instead of the Twitch title', async () => {
    await open();
    await openDialog(0);

    const title = dialog()!.querySelector<HTMLInputElement>('#clip-upload-title')!;

    title.value = 'My best moment';
    title.dispatchEvent(new Event('input', { bubbles: true }));
    await flushPromises();
    await submit();

    expect(posts()[0]!.options?.body).toMatchObject({ title: 'My best moment' });
  });

  it('keeps the dialog open with the reason when the channel is not connected to YouTube', async () => {
    uploadAnswer = () =>
      ok({
        outcome: 'not_connected',
        videoId: null,
        url: null,
        uploadsLeftToday: 97,
      });

    await open();
    await openDialog(0);
    await submit();

    expect(dialog()).not.toBeNull();
    expect(dialog()!.textContent).toContain('This channel is not connected to YouTube.');
    expect(document.body.querySelector('[data-clip-uploaded]')).toBeNull();
  });

  it('says to try again when the bot refuses the upload', async () => {
    uploadAnswer = () => ({ error: { status: 'error' }, response: { status: 503, ok: false } });

    await open();
    await openDialog(0);
    await submit();

    expect(dialog()!.textContent).toContain('The upload did not finish. Try again.');
  });

  it('turns every upload button off when no YouTube uploads are left today', async () => {
    listAnswer = () => ok(list({ uploadsLeftToday: 0 }));

    await open();

    expect(text()).toContain('0 YouTube uploads left today');
    expect(uploadButtons().every((button) => button.disabled)).toBe(true);
  });

  it('reads the next page with the cursor Twitch gave', async () => {
    listAnswer = (cursor) =>
      cursor === 'next-1'
        ? ok(list({ clips: [clip('c3', 'The third one')] }))
        : ok(list({ nextCursor: 'next-1' }));

    await open();
    document.body.querySelector<HTMLButtonElement>('[data-clips-more]')!.click();
    await flushPromises();

    expect(rows()).toHaveLength(3);
    expect(rows()[2]!.textContent).toContain('The third one');
    expect(document.body.querySelector('[data-clips-more]')).toBeNull();
  });

  it('says Twitch lists no clips for a channel without clips', async () => {
    listAnswer = () => ok(list({ clips: [] }));

    await open();

    expect(text()).toContain('Twitch lists no clips for your channel.');
  });

  it('shows an error with a retry when the clips cannot load, not an empty list', async () => {
    listAnswer = () => ({ error: { status: 'error' }, response: { status: 401, ok: false } });

    await open();

    expect(text()).toContain('The clips could not load.');
    expect(text()).not.toContain('Twitch lists no clips');
  });
});
