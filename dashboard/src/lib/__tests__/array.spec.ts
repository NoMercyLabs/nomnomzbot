// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { describe, expect, expectTypeOf, it } from 'vitest'

import { keysOf } from '../array'

describe('keysOf', () => {
  it('returns the own keys in insertion order, typed as the object keys', () => {
    const sizes = { sm: 1, md: 2, lg: 3 } as const

    const keys = keysOf(sizes)

    expect(keys).toEqual(['sm', 'md', 'lg'])
    expectTypeOf(keys).toEqualTypeOf<('sm' | 'md' | 'lg')[]>()
  })
})
