// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'
import { buttonVariants } from '..'

const tokens = readFileSync(resolve(__dirname, '../../../../styles/tokens.css'), 'utf8')
const frame = readFileSync(resolve(__dirname, '../../../../styles/frame.css'), 'utf8')

// The frame.css rule .group/button[data-size='sm'] sets height from --btn-h-sm and beats the h-9 class,
// so the size is decided by the variable, not by the class names (live: 32px at 390 with correct classes).
describe('--btn-h-sm token', () => {
  it('is 36px below md and 32px from md up', () => {
    const base = /:root \{[\s\S]*?--btn-h-sm: (\d+)px;/.exec(tokens)
    const desktop = /@media \(min-width: 768px\) \{\s*:root \{\s*--btn-h-sm: (\d+)px;/.exec(tokens)

    expect(base?.[1]).toBe('36')
    expect(desktop?.[1]).toBe('32')
  })

  it('drives both the sm height and the icon-sm square in frame.css', () => {
    expect(frame).toMatch(/\[data-size='sm'\] \{\s*height: var\(--btn-h-sm\)/)
    expect(frame).toMatch(/\[data-size='icon-sm'\] \{\s*width: var\(--btn-h-sm\);\s*height: var\(--btn-h-sm\)/)
  })
})

// The phone height is the base class and the desktop height is the md: override.
// A max-md: override loses to the base height in the compiled CSS (live: 32px at 390).
describe('buttonVariants touch height', () => {
  it('keeps size sm at 36px on a phone and 32px from md up', () => {
    const classes = buttonVariants({ size: 'sm' }).split(' ')

    expect(classes).toContain('h-9')
    expect(classes).toContain('md:h-8')
    expect(classes).not.toContain('h-8')
  })

  it('keeps size icon-sm at 36px on a phone and 32px from md up', () => {
    const classes = buttonVariants({ size: 'icon-sm' }).split(' ')

    expect(classes).toContain('size-9')
    expect(classes).toContain('md:size-8')
    expect(classes).not.toContain('size-8')
  })
})
