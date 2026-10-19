// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
