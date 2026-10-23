// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { readonly, ref } from 'vue';

// Read a page aloud in the bot's own neural voice, from the server (Stoney 2026-10-05: bot speech is server
// audio only, never the browser's voice). Ported from the NoMercy docs site. The page is split into blocks
// (headings, paragraphs, list items) and each block into sentences. One sentence is one request and one clip,
// the next sentence is fetched while this one plays, and a clip is kept until the reading stops. Pause stops
// the clip and remembers the sentence. When the server cannot speak, the reading stops and says so.

type ReadStatus = 'idle' | 'playing' | 'paused';
type StepUnit = 'paragraph' | 'section';

const BLOCKS = 'h1, h2, h3, h4, p, li, dt, dd, th, td, figcaption';
// A block that holds one of these is read through them, so a list item with a paragraph is not read twice.
const INNER = 'p, h1, h2, h3, h4, dt, dd, figcaption';
const SKIP = 'pre, nav, header, footer, [aria-hidden="true"], [data-read-skip], .sr-only';
// Removed from a block's copy before its text is taken: code, nested lists (read as their own items), controls.
const STRIP = 'pre, ul, ol, table, svg, button, kbd, .sr-only, [aria-hidden="true"]';
const RATES = [0.75, 1, 1.25, 1.5] as const satisfies readonly number[];
const KNOWN_RATES: readonly number[] = RATES;
const RATE_KEY = 'read-aloud-rate';
const FIRST_LINE_GAP = 96;

const status = ref<ReadStatus>('idle');
const rate = ref<number>(1);
// True when the last reading stopped because the server could not speak; the next start clears it.
const failed = ref(false);

let blocks: HTMLElement[] = [];
let sentences: string[] = [];
let blockIndex = 0;
let sentenceIndex = 0;
let language = 'en';
// Each speak() takes a new id. A clip whose end arrives after a pause or a skip carries an old id and is ignored.
let runId = 0;

export function readAloudSupported(): boolean {
  return typeof window !== 'undefined' && 'Audio' in window;
}

function storedRate(): number {
  try {
    const stored = Number(localStorage.getItem(RATE_KEY));

    return KNOWN_RATES.includes(stored)
      ? stored
      : 1;
  } catch {
    return 1;
  }
}

function textOf(element: HTMLElement): string {
  const copy = element.cloneNode(true) as HTMLElement;

  copy.querySelectorAll(STRIP).forEach((node) => node.remove());

  return (copy.textContent ?? '').replace(/\s+/g, ' ').trim();
}

function collect(root: HTMLElement): HTMLElement[] {
  return [...root.querySelectorAll<HTMLElement>(BLOCKS)].filter(
    (element) =>
      !element.closest(SKIP) &&
      !element.querySelector(INNER) &&
      element.getClientRects().length > 0 &&
      textOf(element) !== '',
  );
}

function split(text: string): string[] {
  if ('Segmenter' in Intl) {
    const segmenter: Intl.Segmenter = new Intl.Segmenter(language, { granularity: 'sentence' });

    return [...segmenter.segment(text)].map((part) => part.segment.trim()).filter(Boolean);
  }

  return text.match(/[^.!?]+[.!?]*/g)?.map((part) => part.trim()).filter(Boolean) ?? [text];
}

function isSection(element: HTMLElement): boolean {
  return element.tagName === 'H1' || element.tagName === 'H2';
}

// Keep the spoken block on screen: centered, but never pushed up under the header when it is taller than the
// screen. window.scrollTo, not scrollIntoView, because a smooth scrollIntoView left the page where it was.
function keepInView(element: HTMLElement): void {
  const box = element.getBoundingClientRect();

  if (box.top >= FIRST_LINE_GAP && box.bottom <= window.innerHeight - FIRST_LINE_GAP) return;

  const smooth = !window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  const offset = Math.max(FIRST_LINE_GAP, (window.innerHeight - box.height) / 2);

  window.scrollTo({
    top: window.scrollY + box.top - offset,
    behavior: smooth
      ? 'smooth'
      : 'auto',
  });
}

function mark(element: HTMLElement | undefined): void {
  document.querySelectorAll('[data-reading]').forEach((node) => node.removeAttribute('data-reading'));

  if (!element) return;

  element.setAttribute('data-reading', '');
  keepInView(element);
}

function advance(): void {
  if (sentenceIndex + 1 < sentences.length) {
    sentenceIndex += 1;

    return;
  }

  blockIndex += 1;
  sentenceIndex = 0;
}

const VOICE_URL = '/api/v1/public/read-aloud';

const clips = new Map<string, Promise<string | null>>();
let player: HTMLAudioElement | null = null;

function clipOf(text: string): Promise<string | null> {
  const key = `${language}:${text}`;
  let clip = clips.get(key);

  if (!clip) {
    clip = fetch(VOICE_URL, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ text, locale: language }),
    })
      .then((response) =>
        response.ok
          ? response.blob()
          : null)
      .then((blob) =>
        blob
          ? URL.createObjectURL(blob)
          : null)
      .catch(() => null);

    clips.set(key, clip);
  }

  return clip;
}

function forgetClips(): void {
  clips.forEach((clip) => void clip.then((url) => url && URL.revokeObjectURL(url)));
  clips.clear();
}

function stopSound(): void {
  if (player) {
    player.onended = null;
    player.onerror = null;
    player.pause();
    player = null;
  }
}

// What is read after the current sentence, so its audio can be ready before it is needed.
function upcoming(): string | undefined {
  const next = sentences[sentenceIndex + 1];

  if (next !== undefined)
    return next;

  const block = blocks[blockIndex + 1];

  return block
    ? split(textOf(block))[0]
    : undefined;
}

// The reading stops and the page says the voice is unavailable.
function fail(id: number): void {
  if (id !== runId) return;

  stop();
  failed.value = true;
}

function playClip(url: string, id: number): void {
  const audio = new Audio(url);

  audio.playbackRate = rate.value;
  audio.onended = () => finished(id);
  audio.onerror = () => fail(id);

  player = audio;
  void audio.play().catch(() => fail(id));
}

async function say(sentence: string, id: number): Promise<void> {
  const url = await clipOf(sentence);

  if (id !== runId) return;

  if (url) {
    playClip(url, id);
  } else {
    fail(id);
  }
}

function finished(id: number): void {
  if (id !== runId) return;

  advance();
  speak();
}

function speak(): void {
  runId += 1;
  const id = runId;

  stopSound();

  const block = blocks[blockIndex];

  if (!block) {
    stop();

    return;
  }

  if (sentenceIndex === 0 || sentences.length === 0) {
    sentences = split(textOf(block));
  }

  mark(block);

  // say() asks for this sentence's clip first; the next one is requested right after, so it is ready in time.
  void say(sentences[sentenceIndex] ?? '', id);

  const next = upcoming();

  if (next) {
    void clipOf(next);
  }
}

function jumpTo(index: number): void {
  blockIndex = Math.max(0, Math.min(index, blocks.length - 1));
  sentenceIndex = 0;
  sentences = [];

  if (status.value === 'playing') {
    speak();
  } else {
    mark(blocks[blockIndex]);
  }
}

// Start where the reader is looking, not at the top of the page.
function start(root: HTMLElement, lang: string): void {
  if (!readAloudSupported()) return;

  blocks = collect(root);

  if (blocks.length === 0) return;

  language = lang;
  failed.value = false;

  blockIndex = Math.max(
    blocks.findIndex((block) => block.getBoundingClientRect().bottom > FIRST_LINE_GAP),
    0,
  );

  sentenceIndex = 0;
  sentences = [];
  rate.value = storedRate();
  status.value = 'playing';
  speak();
}

function toggle(): void {
  if (status.value === 'playing') {
    runId += 1;
    stopSound();
    status.value = 'paused';
  } else if (status.value === 'paused') {
    status.value = 'playing';
    speak();
  }
}

function stop(): void {
  runId += 1;

  if (readAloudSupported()) {
    stopSound();
  }

  forgetClips();
  mark(undefined);
  blocks = [];
  sentences = [];
  status.value = 'idle';
}

function step(delta: 1 | -1, unit: StepUnit): void {
  if (unit === 'paragraph') {
    jumpTo(blockIndex + delta);

    return;
  }

  let index: number = blockIndex + delta;

  while (index > 0 && index < blocks.length && !isSection(blocks[index]!)) {
    index += delta;
  }

  jumpTo(index);
}

// The rate is fixed per utterance, so a playing sentence restarts at the new one.
function cycleRate(): void {
  const next = RATES[(KNOWN_RATES.indexOf(rate.value) + 1) % RATES.length]!;

  try {
    localStorage.setItem(RATE_KEY, String(next));
  } catch {
    // The new rate still applies to this visit.
  }

  rate.value = next;

  if (status.value === 'playing') {
    speak();
  }
}

export function useReadAloud() {
  return {
    status: readonly(status),
    rate: readonly(rate),
    failed: readonly(failed),
    supported: readAloudSupported(),
    start,
    toggle,
    stop,
    step,
    cycleRate,
  };
}
