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

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { computed, effectScope, nextTick, ref, type EffectScope } from 'vue';

import { useDraftStorage } from '../useDraftStorage';

const entryKey = (key: string): string => `nomnomz.draft.${key}`;
const text = (key: string): string =>
  localStorage.getItem(entryKey(key)) ?? 'null';
const read = (key: string) =>
  JSON.parse(text(key));

type Open = {
  baseline: ReturnType<typeof ref<string>>;
  value: ReturnType<typeof ref<string[]>>;
  version: ReturnType<typeof ref<string>>;
  key: ReturnType<typeof ref<string>>;
  draft: ReturnType<typeof useDraftStorage<string[]>>;
};

const scopes: EffectScope[] = [];

function open(start: string[], options: { key?: string; version?: string } = {}): Open {
  const scope = effectScope();
  const value = ref<string[]>(start);
  const baseline = ref(JSON.stringify(start));
  const version = ref(options.version ?? 'v1');
  const key = ref(options.key ?? 'c1.p1');
  const dirty = computed(() =>
    JSON.stringify(value.value) !== baseline.value);
  const draft = scope.run(() =>
    useDraftStorage<string[]>(key, value, dirty, version))!;

  scopes.push(scope);

  return {
    baseline,
    value,
    version,
    key,
    draft,
  } as Open;
}

beforeEach(() => {
  localStorage.clear();
  vi.useFakeTimers();
});

afterEach(() => {
  scopes.splice(0).forEach((scope) =>
    scope.stop());

  vi.useRealTimers();
  vi.restoreAllMocks();
});

describe('the draft in local storage', () => {
  it('writes an edit after the debounce, with the version and a timestamp', async () => {
    const editor = open(['a']);

    editor.value.value = ['a', 'b'];
    await nextTick();
    expect(read('c1.p1')).toBeNull();

    vi.advanceTimersByTime(400);

    const stored = read('c1.p1');

    expect(stored.value).toEqual(['a', 'b']);
    expect(stored.version).toBe('v1');
    expect(typeof stored.savedAt).toBe('number');
  });

  it('removes the entry when the value returns to its baseline', async () => {
    const editor = open(['a']);

    editor.value.value = ['a', 'b'];
    await nextTick();
    vi.advanceTimersByTime(400);
    expect(read('c1.p1')).not.toBeNull();

    editor.value.value = ['a'];
    await nextTick();
    vi.advanceTimersByTime(400);

    expect(localStorage.getItem(entryKey('c1.p1'))).toBeNull();
  });

  it('removes the entry when the draft is saved and the version moves on', async () => {
    const editor = open(['a']);

    editor.value.value = ['a', 'b'];
    await nextTick();
    vi.advanceTimersByTime(400);

    expect(read('c1.p1')).not.toBeNull();

    // A save: the saved pipeline now holds the edit and has a new version, so the draft is clean.
    editor.baseline.value = JSON.stringify(['a', 'b']);
    editor.version.value = 'v2';
    await nextTick();
    vi.advanceTimersByTime(400);

    expect(localStorage.getItem(entryKey('c1.p1'))).toBeNull();
  });

  it('removes a stored draft that equals the saved value on open', async () => {
    localStorage.setItem(
      entryKey('c1.p1'),
      JSON.stringify({
        version: 'v0',
        savedAt: 5,
        value: ['a'],
      }),
    );

    const editor = open(['a']);

    vi.advanceTimersByTime(400);

    expect(editor.draft.stored.value).toBeNull();
    expect(localStorage.getItem(entryKey('c1.p1'))).toBeNull();
  });

  it('offers a stored draft on the next open and hands it back once on restore', () => {
    localStorage.setItem(
      entryKey('c1.p1'),
      JSON.stringify({
        version: 'v1',
        savedAt: 5,
        value: ['a', 'x'],
      }),
    );

    const editor = open(['a']);

    expect(editor.draft.stored.value?.value).toEqual(['a', 'x']);
    expect(editor.draft.stale.value).toBe(false);
    expect(editor.draft.restore()).toEqual(['a', 'x']);
    expect(editor.draft.stored.value).toBeNull();
  });

  it('removes the entry on discard and leaves the value alone', () => {
    localStorage.setItem(
      entryKey('c1.p1'),
      JSON.stringify({
        version: 'v1',
        savedAt: 5,
        value: ['a', 'x'],
      }),
    );

    const editor = open(['a']);

    editor.draft.discard();

    expect(editor.draft.stored.value).toBeNull();
    expect(editor.value.value).toEqual(['a']);
    expect(localStorage.getItem(entryKey('c1.p1'))).toBeNull();
  });

  it('keeps the stored draft while an offer is open, so an edit does not erase it', async () => {
    localStorage.setItem(
      entryKey('c1.p1'),
      JSON.stringify({
        version: 'v1',
        savedAt: 5,
        value: ['a', 'x'],
      }),
    );

    const editor = open(['a']);

    editor.value.value = ['a', 'y'];
    await nextTick();
    vi.advanceTimersByTime(400);

    expect(read('c1.p1').value).toEqual(['a', 'x']);
  });

  it('marks a draft from an older version as stale', () => {
    localStorage.setItem(
      entryKey('c1.p1'),
      JSON.stringify({
        version: 'v0',
        savedAt: 5,
        value: ['a', 'x'],
      }),
    );

    const editor = open(['a'], { version: 'v1' });

    expect(editor.draft.stored.value?.value).toEqual(['a', 'x']);
    expect(editor.draft.stale.value).toBe(true);
  });

  it('keeps two pipelines and two channels apart', async () => {
    localStorage.setItem(
      entryKey('c1.p2'),
      JSON.stringify({
        version: 'v1',
        savedAt: 5,
        value: ['other'],
      }),
    );

    localStorage.setItem(
      entryKey('c2.p1'),
      JSON.stringify({
        version: 'v1',
        savedAt: 5,
        value: ['elsewhere'],
      }),
    );

    const editor = open(['a']);

    expect(editor.draft.stored.value).toBeNull();

    editor.value.value = ['a', 'b'];
    await nextTick();
    vi.advanceTimersByTime(400);

    expect(read('c1.p1').value).toEqual(['a', 'b']);
    expect(read('c1.p2').value).toEqual(['other']);
    expect(read('c2.p1').value).toEqual(['elsewhere']);
  });

  it('works without storage when reads and writes throw', async () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });

    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('quota');
    });

    vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(() => {
      throw new Error('blocked');
    });

    const editor = open(['a']);

    expect(editor.draft.stored.value).toBeNull();

    editor.value.value = ['a', 'b'];
    await nextTick();

    expect(() =>
      vi.advanceTimersByTime(400)).not.toThrow();

    expect(editor.value.value).toEqual(['a', 'b']);

    expect(() =>
      editor.draft.discard()).not.toThrow();
  });

  it('ignores a stored entry that is not a draft', () => {
    localStorage.setItem(entryKey('c1.p1'), '{not json');

    expect(open(['a']).draft.stored.value).toBeNull();
  });
});

describe('the unload warning', () => {
  it('prevents the unload while the draft is dirty and not when it is clean', async () => {
    const editor = open(['a']);
    const clean = new Event('beforeunload', { cancelable: true });

    window.dispatchEvent(clean);
    expect(clean.defaultPrevented).toBe(false);

    editor.value.value = ['a', 'b'];
    await nextTick();

    const dirty = new Event('beforeunload', { cancelable: true });

    window.dispatchEvent(dirty);
    expect(dirty.defaultPrevented).toBe(true);

    editor.value.value = ['a'];
    await nextTick();

    const back = new Event('beforeunload', { cancelable: true });

    window.dispatchEvent(back);
    expect(back.defaultPrevented).toBe(false);
  });

  it('writes a pending edit at once when the page unloads inside the debounce', async () => {
    const editor = open(['a']);

    editor.value.value = ['a', 'b'];
    await nextTick();
    window.dispatchEvent(new Event('beforeunload', { cancelable: true }));

    expect(read('c1.p1').value).toEqual(['a', 'b']);
  });
});
