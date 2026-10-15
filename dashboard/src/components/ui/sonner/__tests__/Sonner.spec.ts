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

const source = readFileSync(resolve(__dirname, '../Sonner.vue'), 'utf8')

describe('Sonner wrapper', () => {
  it('loads the vue-sonner stylesheet, without it every toast renders unstyled and off screen', () => {
    expect(source).toContain("import 'vue-sonner/style.css'")
  })

  it('derives its tint from the oklab popover token, not a wrapped hsl() that never parses', () => {
    expect(source).not.toContain('hsl(var(--popover)')
  })
})
