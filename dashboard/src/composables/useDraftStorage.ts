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

// Keeps an editor's unsaved value in this browser, so a reload or a closed tab does not lose it. While the
// value differs from what was saved it is written (after a short pause) under nomnomz.draft.<key>; when it is
// saved or edited back the entry goes. The next open gets the stored draft to restore or discard, never
// applied silently. Storage can be blocked or full: every read and write is guarded and the editor goes on.
import { computed, onScopeDispose, shallowRef, toValue, watch, type MaybeRefOrGetter, type Ref } from 'vue';

export type StoredDraft<T> = {
  // The saved version the draft started from (for a pipeline, its updatedAt).
  version: string;
  savedAt: number;
  value: T;
};

const PREFIX = 'nomnomz.draft.';
const PAUSE_MS = 400;

function isDraft<T>(entry: Partial<StoredDraft<T>> | null): entry is StoredDraft<T> {
  if (typeof entry !== 'object' || entry === null)
    return false;

  return typeof entry.version === 'string' && typeof entry.savedAt === 'number' && 'value' in entry;
}

function readDraft<T>(key: string): StoredDraft<T> | null {
  try {
    const text = localStorage.getItem(PREFIX + key) ?? 'null';
    const parsed = JSON.parse(text);

    return isDraft<T>(parsed)
      ? parsed
      : null;
  } catch {
    return null;
  }
}

function writeDraft<T>(key: string, draft: StoredDraft<T>): void {
  try {
    localStorage.setItem(PREFIX + key, JSON.stringify(draft));
  } catch {
    // Private window, blocked or full storage: the draft just is not kept.
  }
}

function removeDraft(key: string): void {
  try {
    localStorage.removeItem(PREFIX + key);
  } catch {
    // Same as above: nothing to remove when storage is out of reach.
  }
}

export function useDraftStorage<T>(
  key: MaybeRefOrGetter<string | null>,
  value: Ref<T>,
  dirty: MaybeRefOrGetter<boolean>,
  version: MaybeRefOrGetter<string | null | undefined>,
) {
  const found = shallowRef<StoredDraft<T> | null>(null);
  // Decided once per open, when the saved version is known: a later edit must not make the old draft appear.
  const offered = shallowRef<StoredDraft<T> | null>(null);
  let decided = false;
  let timer: ReturnType<typeof setTimeout> | null = null;

  watch(() => toValue(key), (current) => {
    found.value = current
      ? readDraft<T>(current)
      : null;

    offered.value = null;
    decided = false;
  }, { immediate: true });

  watch([found, () => toValue(version)], ([draft, saved]) => {
    if (decided || !draft || saved == null) return;

    decided = true;

    offered.value = JSON.stringify(draft.value) === JSON.stringify(value.value)
      ? null
      : draft;

    // A stored draft equal to what is saved is junk: the next sync removes it.
    if (!offered.value) {
      schedule();
    }
  }, { immediate: true });

  function sync(): void {
    const current = toValue(key);
    const saved = toValue(version);

    // An open offer keeps the stored draft safe: edits made before the choice do not overwrite it.
    if (!current || saved == null || offered.value) return;

    if (toValue(dirty)) {
      writeDraft(current, {
        version: saved,
        savedAt: Date.now(),
        value: value.value,
      });
    } else {
      removeDraft(current);
    }

    found.value = null;
  }

  function flush(): void {
    if (timer === null) return;

    clearTimeout(timer);
    timer = null;
    sync();
  }

  function schedule(): void {
    if (timer !== null) {
      clearTimeout(timer);
    }

    timer = setTimeout(flush, PAUSE_MS);
  }

  watch([
    () =>
      JSON.stringify(value.value),
    () =>
      toValue(dirty),
    () =>
      toValue(version),
    offered,
  ], schedule);

  function warn(event: BeforeUnloadEvent): void {
    flush();
    event.preventDefault();
  }

  watch(() => toValue(dirty), (isDirty, was) => {
    if (isDirty) {
      window.addEventListener('beforeunload', warn);
    } else if (was) {
      window.removeEventListener('beforeunload', warn);
    }
  }, { immediate: true });

  onScopeDispose(() => {
    window.removeEventListener('beforeunload', warn);
    flush();
  });

  function discard(): void {
    const current = toValue(key);

    if (current) {
      removeDraft(current);
    }

    found.value = null;
    offered.value = null;
  }

  // Hands the stored draft over; the caller applies it as one edit. The next change writes it back.
  function restore(): T | null {
    const draft = offered.value;

    offered.value = null;

    return draft
      ? draft.value
      : null;
  }

  return {
    stored: computed(() =>
      offered.value),
    stale: computed(() =>
      offered.value !== null && offered.value.version !== toValue(version)),
    restore,
    discard,
  } as const;
}
