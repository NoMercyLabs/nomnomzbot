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

// A closing dialog, sheet, menu, popover or select hands the focus back to the button that opened it, and the
// browser draws its focus ring there: the "double border" Stoney sent on 2026-10-14 and again on 2026-10-19.
// After a mouse or touch close nobody needs that focus, so it stays off the button. After a keyboard close it goes
// back, ring and all, so a keyboard user keeps their place. Every overlay wrapper in components/ui binds this.
export function returnFocusOnlyForKeyboard(event: Event): void {
  if (lastInput === 'pointer') {
    event.preventDefault();
  }
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
