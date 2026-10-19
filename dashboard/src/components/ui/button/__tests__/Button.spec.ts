// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { describe, expect, it } from 'vitest'
import { buttonVariants } from '..'

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
