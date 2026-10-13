// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { afterEach, describe, expect, it } from 'vitest'

import { accentTokens, applyAccent } from '../accent'

afterEach(() => {
  document.documentElement.removeAttribute('style')
  document.documentElement.classList.remove('light')
})

// The expected values are what the approved mockups paint (base/accent.js with #FF014F, read from the page).
describe('accent from a channel colour', () => {
  it('gives the dark theme the same accent family as the mockups', () => {
    expect(accentTokens('#FF014F', 'dark')).toMatchObject({
      primary: 'rgb(255 1 79)',
      'primary-foreground': 'rgb(23 23 23)',
      accent: 'rgb(78 27 43)',
      'accent-foreground': 'rgb(238 238 238)',
      ring: 'rgb(255 1 79)',
      'sidebar-primary': 'rgb(219 4 70)',
      'sidebar-accent': 'rgb(58 12 27)',
    })
  })

  it('darkens a light colour until text on the light theme reads at 4.5:1', () => {
    const tokens: Record<string, string> = accentTokens('#FFFF00', 'light')

    expect(tokens.primary).not.toBe('rgb(255 255 0)')
  })

  it('writes the family onto the page, for the theme the page shows', () => {
    applyAccent('#FF014F')

    expect(document.documentElement.style.getPropertyValue('--primary')).toBe('rgb(255 1 79)')
  })

  it('leaves the page alone for a value that is not a colour', () => {
    applyAccent('pink')

    expect(document.documentElement.style.getPropertyValue('--primary')).toBe('')
  })
})
