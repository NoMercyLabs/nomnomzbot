// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { RuleTester } from '@typescript-eslint/rule-tester'
import { afterAll, describe, it } from 'vitest'

import { noRedundantTypeAnnotation } from '../no-redundant-type-annotation'

RuleTester.afterAll = afterAll
RuleTester.describe = describe
RuleTester.it = it
RuleTester.itOnly = it.only

const tester = new RuleTester({
  languageOptions: {
    parserOptions: {
      projectService: { allowDefaultProject: ['*.ts'] },
      tsconfigRootDir: import.meta.dirname,
    },
  },
})

tester.run('no-redundant-type-annotation', noRedundantTypeAnnotation, {
  valid: [
    // Widens: an empty list, a string-keyed table, an index over a bare object.
    'const ids: string[] = []',
    'declare const name: string; const scores: Record<string, number> = { a: 1 }; scores[name]',
    'export const shared: Record<string, number> = { a: 1 }',
    'declare function props(): {}; const given: Record<string, unknown> = props()',
    // Narrows through a generic that only the annotation feeds.
    "const root: HTMLElement | null = document.querySelector('.x')",
    'const later: Promise<number> = new Promise((done) => done(1))',
    // Gives a bare arrow its parameter types.
    'const label: (n: number) => string = (n) => String(n)',
    // Names the type a loop narrows.
    'let page: number | null = 1; while (page !== null) { const current: number = page; page = current > 3 ? null : current + 1 }',
    // A `let` may be reassigned with a wider value.
    'let total: number = Math.max(1, 2)',
    // A table whose value has an optional field keeps it readable on every key.
    "type Row = { label: string; hint?: string }; const rows: Record<'a' | 'b', Row> = { a: { label: 'A' }, b: { label: 'B', hint: 'h' } }",
    // `as const` would reject the write or freeze the list.
    'const counter: { seen: number } = { seen: 0 }; counter.seen++',
    'const bucket: { seen: string[] } = { seen: [] }',
  ],
  invalid: [
    {
      code: "type Theme = { bg: string; fg: string }; const dark: Theme = { bg: '#000', fg: '#fff' }",
      output:
        "type Theme = { bg: string; fg: string }; const dark = { bg: '#000', fg: '#fff' } as const satisfies Theme",
      errors: [{ messageId: 'table' }],
    },
    {
      code: 'class QueryClient { size = 1 }; declare function useQueryClient(): QueryClient; const queries: QueryClient = useQueryClient()',
      output:
        'class QueryClient { size = 1 }; declare function useQueryClient(): QueryClient; const queries = useQueryClient()',
      errors: [{ messageId: 'redundant' }],
    },
    {
      code: 'const biggest: number = Math.max(1, 2)',
      output: 'const biggest = Math.max(1, 2)',
      errors: [{ messageId: 'redundant' }],
    },
    {
      code: "type Preset = 'calm' | 'loud'; const volume: Record<Preset, number> = { calm: 1, loud: 9 }",
      output:
        "type Preset = 'calm' | 'loud'; const volume = { calm: 1, loud: 9 } as const satisfies Record<Preset, number>",
      errors: [{ messageId: 'table' }],
    },
    {
      code: "const sizes: Record<string, { px: number }> = { sm: { px: 14 } }; for (const [name, size] of Object.entries(sizes)) console.log(name, size.px, sizes.sm, sizes['sm'])",
      output:
        "const sizes = { sm: { px: 14 } } as const satisfies Record<string, { px: number }>; for (const [name, size] of Object.entries(sizes)) console.log(name, size.px, sizes.sm, sizes['sm'])",
      errors: [{ messageId: 'table' }],
    },
  ],
})
