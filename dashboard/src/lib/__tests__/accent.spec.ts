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

import { afterEach, describe, expect, it } from 'vitest';

import { accentTokens, applyAccent } from '../accent';

afterEach(() => {
  document.documentElement.removeAttribute('style');
  document.documentElement.classList.remove('light');
});

// The expected values are what the approved mockups paint (base/accent.js with #FF014F, read from the page).
describe('accent from a channel color', () => {
  it('gives the dark theme the same accent family as the mockups', () => {
    expect(accentTokens('#FF014F', 'dark')).toMatchObject({
      primary: 'rgb(255 1 79)',
      'primary-foreground': 'rgb(250 250 250)',
      accent: 'rgb(78 27 43)',
      'accent-foreground': 'rgb(238 238 238)',
      ring: 'rgb(255 1 79)',
      'sidebar-primary': 'rgb(219 4 70)',
      'sidebar-accent': 'rgb(58 12 27)',
    });
  });

  // White text on an accent button, as the owner asked (2026-10-14); black only where white cannot be read.
  it('writes white on a color that carries white, and black on a color too light for it', () => {
    expect(accentTokens('#9146FF', 'dark')['primary-foreground']).toBe('rgb(250 250 250)');
    expect(accentTokens('#FFD700', 'dark')['primary-foreground']).toBe('rgb(23 23 23)');
  });

  it('darkens a light color until text on the light theme reads at 4.5:1', () => {
    const tokens: Record<string, string> = accentTokens('#FFFF00', 'light');

    expect(tokens.primary).not.toBe('rgb(255 255 0)');
  });

  it('writes the family onto the page, for the theme the page shows', () => {
    applyAccent('#FF014F');

    expect(document.documentElement.style.getPropertyValue('--primary')).toBe('rgb(255 1 79)');
  });

  it('leaves the page alone for a value that is not a color', () => {
    applyAccent('pink');

    expect(document.documentElement.style.getPropertyValue('--primary')).toBe('');
  });
});
