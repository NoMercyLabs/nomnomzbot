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

// The kind of input that moved last: a pointer (mouse, touch, pen) or the keyboard.
let lastInput: 'keyboard' | 'pointer' = 'keyboard';

if (typeof window !== 'undefined') {
  window.addEventListener('pointerdown', () => {
    lastInput = 'pointer';
  }, { capture: true, passive: true });

  window.addEventListener('keydown', () => {
    lastInput = 'keyboard';
  }, { capture: true, passive: true });
}

// `focusVisible` is in the HTML standard but not yet in the TypeScript DOM types.
interface QuietFocusOptions extends FocusOptions {
  focusVisible: boolean;
}

interface Opener {
  element: HTMLElement;
  input: 'keyboard' | 'pointer';
}

// The button that opened each overlay and the input that pressed it, found by the overlay's focus scope.
const openers = new WeakMap<EventTarget, Opener>();

// A Select emits no open event, and a mouse open never focuses its trigger, so rememberOpener cannot see it (fault
// 25b: the Escape close then drew the ring on the trigger). Its wrapper names the trigger and the id of the list it
// controls when it opens; the close finds the opener by that list inside the closing focus scope.
const controlOpeners = new Map<string, Opener>();

export function rememberControlOpener(element: HTMLElement, controlsId: string): void {
  controlOpeners.set(controlsId, { element, input: lastInput });
}

function takeControlOpener(scope: EventTarget): Opener | undefined {
  if (!(scope instanceof Element))
    return undefined;

  for (const [id, opener] of controlOpeners) {
    if (scope.id === id || scope.querySelector(`[id="${id}"]`)) {
      controlOpeners.delete(id);

      return opener;
    }
  }

  return undefined;
}

function takeOpener(scope: EventTarget): Opener | undefined {
  const opener = openers.get(scope);

  openers.delete(scope);

  return opener ?? takeControlOpener(scope);
}

// Runs when an overlay opens, before its focus moves: the focused element is the opener.
export function rememberOpener(event: Event): void {
  const element = document.activeElement;

  if (event.target && element instanceof HTMLElement && element !== document.body) {
    openers.set(event.target, { element, input: lastInput });
  }
}

// A closing dialog, sheet, menu, popover or select hands the focus back to the button that opened it, and the
// browser draws its focus ring there: the "double border" Stoney sent on 2026-10-14 and again on 2026-10-19.
// When a mouse or touch opened the overlay, the focus still goes back (a person keeps their place) but quietly,
// with no ring, whichever input closed it. When the keyboard opened it, the focus goes back with its ring, so a
// keyboard user keeps their place. Every overlay wrapper in components/ui binds this to close-auto-focus.
export function returnFocusOnlyForKeyboard(event: Event): void {
  const opener = event.target
    ? takeOpener(event.target)
    : undefined;

  if (!opener) {
    if (lastInput === 'pointer') {
      event.preventDefault();
    }

    return;
  }

  if (opener.input === 'keyboard' || !opener.element.isConnected) return;

  event.preventDefault();
  const quiet: QuietFocusOptions = { preventScroll: true, focusVisible: false };

  opener.element.focus(quiet);
}

// What the browser tabs to first inside a panel, and what counts as a text field among those.
const tabbableControl =
  'a[href], button:not(:disabled), input:not(:disabled):not([type="hidden"]), select:not(:disabled), textarea:not(:disabled), [tabindex]:not([tabindex="-1"]), [contenteditable]:not([contenteditable="false"])';
const textField = 'input, textarea, select, [contenteditable]:not([contenteditable="false"])';

// An opening dialog, sheet, drawer or popover moves the focus to its first control, and the browser can draw a focus
// ring there right after a mouse click: the ring on "Readability" that Stoney sent on 2026-10-20. After a mouse or
// touch open the focus goes to the panel itself instead, so no ring shows. A text field first in the panel still
// takes the focus, so a person can type at once. After a keyboard open the first control keeps it. Every focus-scope
// wrapper in components/ui binds this to open-auto-focus; menus and selects already focus their own content.
export function focusPanelOnPointerOpen(event: Event): void {
  rememberOpener(event);

  const scope = event.target;

  if (lastInput !== 'pointer' || !(scope instanceof HTMLElement)) return;

  const first = scope.querySelector<HTMLElement>(tabbableControl);

  if (first?.matches(textField)) return;

  event.preventDefault();

  // A popover's focus scope is the positioning wrapper, which has no tabindex; the panel inside it does.
  const holder = scope.hasAttribute('tabindex')
    ? scope
    : scope.querySelector<HTMLElement>('[tabindex="-1"]');

  (holder ?? scope).focus({ preventScroll: true });
}
