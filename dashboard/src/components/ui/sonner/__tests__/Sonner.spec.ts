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
