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

  it('has no utils, helpers or misc file or folder besides lib/utils.ts and the shadcn-vue output', () => {
    const offenders: string[] = allPaths(src)
      .map((path) => relative(src, path).replace(/\\/g, '/'))
      .filter((path) => dumpingGround.test(path.split('/').pop() ?? ''))
      .filter((path) => path !== 'lib/utils.ts' && !path.startsWith('components/ui/'))

    expect(offenders).toEqual([])
  })

  // The shadcn-vue CLI adds a Google Fonts import on every `add`; Inter is bundled from npm instead.
  it('loads no style from another origin', () => {
    const remote: string[] = allPaths(join(src, 'styles'))
      .filter((path) => path.endsWith('.css'))
      .filter((path) => /@import\s+url\(\s*['"]?https?:/.test(readFileSync(path, 'utf8')))
      .map((path) => relative(src, path).replace(/\\/g, '/'))

    expect(remote).toEqual([])
  })

  // V18: Pinia holds local state only; everything from the API lives in TanStack Query (V6).
  it('keeps API data out of the Pinia stores', () => {
    const fetching: string[] = allPaths(join(src, 'stores'))
      .filter((path) => path.endsWith('.ts') && !path.includes('__tests__'))
      .filter((path) =>
        /from ['"](@\/api\/|\.\.\/api\/|@tanstack\/)/.test(readFileSync(path, 'utf8')),
      )
      .map((path) => relative(src, path).replace(/\\/g, '/'))

    expect(fetching).toEqual([])
  })

  // Stoney 2026-10-13 (translated from Dutch): "the point is that we do not use Google with the privacy-invading junk".
  it('calls no Google font or tracking host from any source file or the page shell', () => {
    const google: RegExp =
      /googleapis\.com|gstatic\.com|google-analytics\.com|googletagmanager\.com/
    const offenders: string[] = [...allPaths(src), join(src, '..', 'index.html')]
      .filter((path) => /\.(vue|ts|css|html|json)$/.test(path) && !path.endsWith('schema.d.ts'))
      .filter((path) => google.test(readFileSync(path, 'utf8')))
      .map((path) => relative(join(src, '..'), path).replace(/\\/g, '/'))

    expect(offenders).toEqual([])
  })

  // The CLI parts carry English screen-reader labels, and the raw-text lint is off in components/ui.
  // A caller hides the built-in close button and brings a translated one, and uses its own
  // translated button instead of SidebarTrigger or BreadcrumbEllipsis. DialogScrollContent cannot
  // hide its close label, so it is not used.
  it('shows no untranslated shadcn label on any page', () => {
    const ownFiles: string[] = allPaths(src).filter(
      (path) =>
        path.endsWith('.vue') &&
        !relative(src, path).replace(/\\/g, '/').startsWith('components/ui/'),
    )
    const untranslated: string[] = ownFiles
      .filter((path) => {
        const text: string = readFileSync(path, 'utf8')
        const closable: string[] = text.match(/<(SheetContent|DialogContent)\b[^>]*>/g) ?? []
        return (
          closable.some((tag) => !tag.includes(':show-close-button="false"')) ||
          /<(SidebarTrigger|BreadcrumbEllipsis|DialogScrollContent)\b/.test(text) ||
          /<DialogFooter\b[^>]*show-close-button(?!="false")/.test(text)
        )
      })
      .map((path) => relative(src, path).replace(/\\/g, '/'))

    expect(untranslated).toEqual([])
  })

  // styles/frame.css gives harm one look: variant="destructive". A red class on another variant
  // makes a second harm style (a red underlined link) that the frame rules never approved.
  it('marks a harmful button only with the destructive variant, never a red class', () => {
    const tinted: string[] = allPaths(src)
      .filter(
        (path) =>
          path.endsWith('.vue') &&
          !relative(src, path).replace(/\\/g, '/').startsWith('components/ui/'),
      )
      .filter((path) =>
        /<Button\b[^>]*\bclass="[^"]*\btext-destructive\b/.test(readFileSync(path, 'utf8')),
      )
      .map((path) => relative(src, path).replace(/\\/g, '/'))

    expect(tinted).toEqual([])
  })

  // A page that builds its own segmented control loses the shared look (a settings copy showed no chosen
  // option at all). Pages use SegmentedControl or FilterBar from components/layout.
  it('builds a segmented control only inside the shared layout blocks', () => {
    const own: string[] = allPaths(src)
      .map((path) => relative(src, path).replace(/\\/g, '/'))
      .filter(
        (path) =>
          path.endsWith('.vue') &&
          !path.startsWith('components/ui/') &&
          !path.startsWith('components/layout/'),
      )
      .filter((path) =>
        readFileSync(join(src, path), 'utf8').includes('@/components/ui/toggle-group'),
      )

    expect(own).toEqual([])
  })

  // Stoney 2026-10-13: "please hardcode the background color for light and darkmode systems so i don't get
  // flashbang'd". The page shell paints the theme background before any script or stylesheet loads.
  it('paints the theme background in the page shell before any stylesheet loads', () => {
    const shell: string = readFileSync(join(src, '..', 'index.html'), 'utf8')
    const tokens: string = readFileSync(join(src, 'styles', 'tokens.css'), 'utf8')
    const dark: string = /:root\s*\{[^}]*--background:\s*([^;]+);/.exec(tokens)?.[1] ?? 'missing'
    const head: string = shell.slice(0, shell.indexOf('</head>'))

    expect(head).toMatch(/<meta name="color-scheme" content="dark"\s*\/?>/)
    expect(head).toMatch(
      new RegExp(`<style>[^<]*html\\s*\\{[^}]*background:\\s*${dark.replace(/[()]/g, '\\$&')}`),
    )
  })

  // Stoney 2026-10-13: "If I have to correct you on icons then you're fired" and "use the moooom icons
  // from my designer". Icons come from the designer pack (`~icons/nnz/<name>`, assets/icons/line); brand
  // logos from simple-icons, drawn by PlatformLogo from the library's own path. Lucide stays only inside
  // the shadcn-vue CLI output. A hand-drawn svg is a guess.
  it('takes every icon from the designer pack: no Lucide import and no hand-drawn svg', () => {
    const own: string[] = allPaths(src)
      .map((path) => relative(src, path).replace(/\\/g, '/'))
      .filter((path) => /\.(vue|ts)$/.test(path) && !path.startsWith('components/ui/'))
    const offenders: string[] = own.filter((path) => {
      const text: string = readFileSync(join(src, path), 'utf8')
      const drawn: boolean =
        path !== 'components/layout/PlatformLogo.vue' &&
        path.endsWith('.vue') &&
        /<svg\b/.test(text)
      return drawn || /from ['"](@lucide\/vue|lucide-vue-next)['"]/.test(text)
    })

    expect(offenders).toEqual([])
  })

  // Stoney 2026-10-13 (translated from Dutch): "you are really going to town with all those em dashes". Copy uses plain sentences.
  it('writes no em dash in any words file', () => {
    const dashed: string[] = allPaths(src)
      .filter((path) => path.endsWith('.json') && /[\\/](i18n|locales)[\\/]/.test(path))
      .filter((path) => readFileSync(path, 'utf8').includes('—'))
      .map((path) => relative(src, path).replace(/\\/g, '/'))

    expect(dashed).toEqual([])
  })

  it('gives each page translation file only the keys of its own page', () => {
    const misplaced: string[] = allPaths(join(src, 'views'))
      .map((path) => relative(src, path).replace(/\\/g, '/'))
      .filter((path) => /^views\/[^/]+\/[^/]+\/i18n\/[^/]+\.json$/.test(path))
      .flatMap((path) => {
        const [, area, slug] = path.split('/')
        const words: Record<string, Record<string, unknown>> = JSON.parse(
          readFileSync(join(src, path), 'utf8'),
        )
        const keys: string[] = Object.entries(words).flatMap(([top, pages]) =>
          Object.keys(pages).map((page) => `${top}.${page}`),
        )
        return keys.filter((key) => key !== `${area}.${slug}`).map((key) => `${path}: ${key}`)
      })

    expect(misplaced).toEqual([])
  })
})
