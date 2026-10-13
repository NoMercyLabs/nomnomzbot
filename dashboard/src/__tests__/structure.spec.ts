// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { readdirSync, readFileSync } from 'node:fs'
import { join, relative } from 'node:path'
import { describe, expect, it } from 'vitest'

import * as utils from '../lib/utils'

const src: string = join(__dirname, '..')
const dumpingGround: RegExp = /^(utils?|helpers?|misc|common)(\.\w+)?$/i

function allPaths(folder: string): string[] {
  return readdirSync(folder, { withFileTypes: true }).flatMap((entry) => {
    const path: string = join(folder, entry.name)
    return entry.isDirectory() ? [path, ...allPaths(path)] : [path]
  })
}

// Section 2a of frontend-vue-foundation.md: code lives by topic, never in a grab bag.
describe('folder layout', () => {
  it('keeps lib/utils.ts to the cn() that shadcn-vue needs', () => {
    expect(Object.keys(utils)).toEqual(['cn'])
  })

  it('has no utils, helpers or misc file or folder besides lib/utils.ts', () => {
    const offenders: string[] = allPaths(src)
      .map((path) => relative(src, path).replace(/\\/g, '/'))
      .filter((path) => dumpingGround.test(path.split('/').pop() ?? ''))
      .filter((path) => path !== 'lib/utils.ts')

    expect(offenders).toEqual([])
  })

  it('gives each page translation file only the keys of its own page', () => {
    const misplaced: string[] = allPaths(join(src, 'views'))
      .map((path) => relative(src, path).replace(/\\/g, '/'))
      .filter((path) => /^views\/[^/]+\/[^/]+\/i18n\/[^/]+\.json$/.test(path))
      .flatMap((path) => {
        const [, area, slug] = path.split('/')
        const words: Record<string, Record<string, unknown>> = JSON.parse(readFileSync(join(src, path), 'utf8'))
        const keys: string[] = Object.entries(words).flatMap(([top, pages]) =>
          Object.keys(pages).map((page) => `${top}.${page}`),
        )
        return keys.filter((key) => key !== `${area}.${slug}`).map((key) => `${path}: ${key}`)
      })

    expect(misplaced).toEqual([])
  })
})
