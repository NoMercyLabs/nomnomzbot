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

import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { afterEach, beforeEach, describe, expect, it, type Mock, vi } from 'vitest';

import { i18n } from '@/i18n';

import ReadAloud from '../ReadAloud.vue';

class FakeAudio {
  static played: FakeAudio[] = [];
  static refusePlay = false;

  url: string;
  playbackRate = 1;
  paused = false;
  onended: (() => void) | null = null;
  onerror: ((event: Event) => void) | null = null;

  constructor(url: string) {
    this.url = url;
  }

  play(): Promise<void> {
    FakeAudio.played.push(this);

    return FakeAudio.refusePlay
      ? Promise.reject(new Error('blocked'))
      : Promise.resolve();
  }

  pause(): void {
    this.paused = true;
  }
}

let mounted: VueWrapper | null = null;
let page: HTMLElement | null = null;

type Answer = { ok: boolean; blob: () => Promise<Blob | null> };
type Ask = (url: string, init: { body: string }) => Promise<Answer>;

// The server's answer to each sentence: a clip, or a refusal.
function serverAnswers(ok: boolean): Mock<Ask> {
  const answer = vi.fn<Ask>(() =>
    Promise.resolve({ ok, blob: () => Promise.resolve(ok
      ? new Blob(['mp3'])
      : null) }),
  );

  vi.stubGlobal('fetch', answer);

  return answer;
}

let fetcher = serverAnswers(true);

function lastClip(): FakeAudio {
  return FakeAudio.played[FakeAudio.played.length - 1]!;
}

async function clipEnds(): Promise<void> {
  lastClip().onended?.();
  await flushPromises();
}

async function tap(selector: string): Promise<void> {
  document.querySelector<HTMLElement>(selector)!.click();
  await flushPromises();
}

function reading(): string | undefined {
  return document.querySelector('[data-reading]')?.textContent ?? undefined;
}

function sentBody(init: unknown): { text: string; locale: string } {
  return JSON.parse((init as { body: string }).body);
}

function askedFor(): string[] {
  return fetcher.mock.calls.map(([, init]) => sentBody(init).text);
}

async function open(): Promise<void> {
  page = document.createElement('main');

  page.innerHTML = `
    <h1>Privacy Policy</h1>
    <h2>First</h2>
    <p>One is first. Two is second.</p>
    <pre>code stays silent</pre>
    <h2>Second</h2>
    <p>Three is third.</p>
  `;

  document.body.appendChild(page);

  mounted = mount(ReadAloud, { global: { plugins: [i18n] }, attachTo: document.body });
  await flushPromises();
}

beforeEach(() => {
  FakeAudio.played = [];
  FakeAudio.refusePlay = false;
  localStorage.clear();

  // The server answers every sentence with a clip.
  fetcher = serverAnswers(true);
  vi.stubGlobal('Audio', FakeAudio);
  Object.defineProperty(window, 'Audio', { configurable: true, value: FakeAudio });
  URL.createObjectURL = vi.fn<(blob: Blob) => string>(() => 'blob:clip');
  URL.revokeObjectURL = vi.fn<(url: string) => void>();
  window.scrollTo = vi.fn<() => void>();

  window.matchMedia = vi.fn<(query: string) => { matches: boolean }>(() => ({
    matches: true,
  })) as unknown as typeof window.matchMedia;

  // jsdom has no layout: say every element is on screen so the page has something to read.
  vi.spyOn(Element.prototype, 'getClientRects').mockReturnValue([{}] as unknown as DOMRectList);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  page?.remove();
  page = null;
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

// Stoney 2026-10-23: "i want a proper voice that knows how to speak". Bot speech is server audio only
// (Stoney 2026-10-05), so the page never uses the browser's own voice.
describe('Read aloud', () => {
  it('reads in the bot voice, one sentence per request, skipping code, and the highlight follows', async () => {
    await open();
    await tap('[data-read-aloud-start]');

    expect(FakeAudio.played).toHaveLength(1);
    expect(reading()).toBe('Privacy Policy');

    while (reading() !== 'Three is third.' && FakeAudio.played.length < 20) {
      await clipEnds();
    }

    expect(reading()).toBe('Three is third.');

    expect(askedFor()).toEqual([
      'Privacy Policy',
      'First',
      'One is first.',
      'Two is second.',
      'Second',
      'Three is third.',
    ]);
  });

  it('asks the server for the page language with every sentence', async () => {
    await open();
    await tap('[data-read-aloud-start]');

    const body = sentBody(fetcher.mock.calls[0]![1]);

    expect(fetcher.mock.calls[0]![0]).toBe('/api/v1/public/read-aloud');
    expect(body.locale).toBe('en');
  });

  it('fetches the next sentence while the current one plays', async () => {
    await open();
    await tap('[data-read-aloud-start]');

    expect(askedFor()).toEqual(['Privacy Policy', 'First']);
  });

  it('pauses on the same sentence and goes on from it', async () => {
    await open();
    await tap('[data-read-aloud-start]');
    await clipEnds();
    expect(reading()).toBe('First');

    await tap('[data-read-toggle]');
    const paused = lastClip();

    expect(paused.paused).toBe(true);
    expect(document.querySelector('[data-read-toggle]')?.getAttribute('aria-label')).toBe('Play');

    // A clip that ends after the pause belongs to an old run and changes nothing.
    const before = FakeAudio.played.length;

    paused.onended?.();
    await flushPromises();
    expect(FakeAudio.played).toHaveLength(before);

    await tap('[data-read-toggle]');

    expect(FakeAudio.played).toHaveLength(before + 1);
    expect(reading()).toBe('First');
  });

  it('jumps by section and by paragraph', async () => {
    await open();
    await tap('[data-read-aloud-start]');

    await tap('[aria-label="Next section"]');
    expect(reading()).toBe('First');

    await tap('[aria-label="Next section"]');
    expect(reading()).toBe('Second');

    await tap('[aria-label="Previous paragraph"]');
    expect(reading()).toBe('One is first. Two is second.');
  });

  // Stoney 2026-10-29: "changing the speed should not restart the phrase".
  it('cycles the speed, keeps it, and changes the speed of the sentence that is playing', async () => {
    await open();
    await tap('[data-read-aloud-start]');
    const playing = lastClip();
    const clipsBefore = FakeAudio.played.length;

    await tap('[data-read-rate]');

    expect(lastClip()).toBe(playing);
    expect(FakeAudio.played).toHaveLength(clipsBefore);
    expect(playing.paused).toBe(false);
    expect(playing.playbackRate).toBe(1.25);
    expect(localStorage.getItem('read-aloud-rate')).toBe('1.25');
  });

  it('plays the next sentence at the speed chosen while the one before it was playing', async () => {
    await open();
    await tap('[data-read-aloud-start]');
    await tap('[data-read-rate]');
    await clipEnds();

    expect(lastClip().playbackRate).toBe(1.25);
  });

  it('stops on Escape, clears the highlight and brings the start button back', async () => {
    await open();
    await tap('[data-read-aloud-start]');
    const clip = lastClip();

    expect(document.querySelector('[data-read-bar]')).not.toBeNull();

    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    await flushPromises();

    expect(clip.paused).toBe(true);
    expect(document.querySelector('[data-read-bar]')).toBeNull();
    expect(document.querySelector('[data-reading]')).toBeNull();
    expect(document.querySelector('[data-read-aloud-start]')).not.toBeNull();
  });

  it('frees the stored clips when the reading stops', async () => {
    await open();
    await tap('[data-read-aloud-start]');

    await tap('[data-read-stop]');

    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:clip');
  });

  it('stops the voice when the page is left', async () => {
    await open();
    await tap('[data-read-aloud-start]');
    const clip = lastClip();

    mounted?.unmount();
    mounted = null;

    expect(clip.paused).toBe(true);
  });

  it('says the voice is unavailable, and never falls back to the browser voice, when the server cannot speak', async () => {
    fetcher = serverAnswers(false);

    await open();
    await tap('[data-read-aloud-start]');

    expect(FakeAudio.played).toHaveLength(0);
    expect(document.querySelector('[data-read-bar]')).toBeNull();
    expect(document.querySelector('[data-read-failed]')?.textContent).toContain('not available');
    expect(document.querySelector('[data-read-aloud-start]')).not.toBeNull();
  });

  it('says the voice is unavailable when the browser refuses to play the clip', async () => {
    FakeAudio.refusePlay = true;

    await open();
    await tap('[data-read-aloud-start]');

    expect(document.querySelector('[data-read-failed]')).not.toBeNull();
    expect(document.querySelector('[data-read-bar]')).toBeNull();
  });

  it('clears the unavailable line when the reader tries again', async () => {
    fetcher = serverAnswers(false);

    await open();
    await tap('[data-read-aloud-start]');
    expect(document.querySelector('[data-read-failed]')).not.toBeNull();

    fetcher = serverAnswers(true);
    await tap('[data-read-aloud-start]');

    expect(document.querySelector('[data-read-failed]')).toBeNull();
    expect(document.querySelector('[data-read-bar]')).not.toBeNull();
  });

  it('offers no button when the browser cannot play audio', async () => {
    Reflect.deleteProperty(window, 'Audio');
    await open();

    expect(document.querySelector('[data-read-aloud-start]')).toBeNull();
  });
});
