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

// The keyboard shortcuts of the dashboard. A part that offers one registers it here while it is on screen, so
// the "?" list always names exactly the shortcuts the page has. Stoney 2026-10-14: "shortcuts for the chat
// input and other important ones too like search is /".
import { useEventListener } from '@vueuse/core';
import { onScopeDispose, shallowRef } from 'vue';

export interface Shortcut {
  // The key as KeyboardEvent.key reports it: '/', '?', 'e', 'Escape'.
  key: string;
  // Ctrl on Windows and Linux, Cmd on a Mac.
  mod?: boolean;
  // The words key of what it does, shown in the "?" list.
  label: string;
  // Also fires while the person types in a field; plain letters never should.
  inFields?: boolean;
}

const active = shallowRef<readonly Shortcut[]>([]);

export function useShortcutList(): Readonly<typeof active> {
  return active;
}

export function isTyping(target: EventTarget | null): boolean {
  return target instanceof HTMLElement &&
    (target.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(target.tagName));
}

function matches(shortcut: Shortcut, event: KeyboardEvent): boolean {
  const mod = event.ctrlKey || event.metaKey;

  return event.key.toLowerCase() === shortcut.key.toLowerCase() &&
    mod === (shortcut.mod ?? false) &&
    !event.altKey &&
    (shortcut.inFields === true || !isTyping(event.target));
}

// The key cap text: "Ctrl E", or "⌘ E" on a Mac.
export function keyCaps(shortcut: Shortcut): string[] {
  const mac = /Mac|iPhone|iPad/.test(navigator.userAgent);
  const key = shortcut.key.length === 1
    ? shortcut.key.toUpperCase()
    : shortcut.key;

  if (!shortcut.mod)
    return [key];

  return [mac
    ? '⌘'
    : 'Ctrl', key];
}

export function useShortcut(shortcut: Shortcut, run: (event: KeyboardEvent) => void): void {
  active.value = [...active.value, shortcut];

  useEventListener(document, 'keydown', (event: KeyboardEvent): void => {
    if (event.defaultPrevented || !matches(shortcut, event)) return;

    event.preventDefault();
    run(event);
  });

  onScopeDispose(() => {
    active.value = active.value.filter((one) =>
      one !== shortcut);
  });
}
