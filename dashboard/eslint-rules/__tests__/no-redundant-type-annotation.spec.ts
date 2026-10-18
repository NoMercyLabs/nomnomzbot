// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { RuleTester } from '@typescript-eslint/rule-tester';
import { afterAll, describe, it } from 'vitest';

import { noRedundantTypeAnnotation } from '../no-redundant-type-annotation';

RuleTester.afterAll = afterAll;
RuleTester.describe = describe;
RuleTester.it = it;
RuleTester.itOnly = it.only;

const tester = new RuleTester({
  languageOptions: {
    parserOptions: {
      projectService: { allowDefaultProject: ['*.ts'] },
      tsconfigRootDir: import.meta.dirname,
    },
  },
});

tester.run('no-redundant-type-annotation', noRedundantTypeAnnotation, {
  valid: [
    // Widens: an empty list, a string-keyed table, an index over a bare object.
    'const ids: string[] = []',
    'declare const name: string; const scores: Record<string, number> = { a: 1 }; scores[name]',
    'export const shared: Record<string, number> = { a: 1 }',
    'declare function props(): {}; const given: Record<string, unknown> = props()',
    // Narrows through a generic that only the annotation feeds.
    'const root: HTMLElement | null = document.querySelector(\'.x\')',
    'const later: Promise<number> = new Promise((done) => done(1))',
    // Gives a bare arrow its parameter types.
    'const label: (n: number) => string = (n) => String(n)',
    // Names the type a loop narrows.
    'let page: number | null = 1; while (page !== null) { const current: number = page; page = current > 3 ? null : current + 1 }',
    // A `let` may be reassigned with a wider value.
    'let total: number = Math.max(1, 2)',
    // A table whose value has an optional field keeps it readable on every key.
    'type Row = { label: string; hint?: string }; const rows: Record<\'a\' | \'b\', Row> = { a: { label: \'A\' }, b: { label: \'B\', hint: \'h\' } }',
    // `as const` would reject the write or freeze the list.
    'const counter: { seen: number } = { seen: 0 }; counter.seen++',
    'const bucket: { seen: string[] } = { seen: [] }',
    // ScriptsView FILTERS: read by a computed string key, `as const` would reject `FILTERS[tab]`.
    'type Script = { on: boolean }; declare const tab: string; const FILTERS: Readonly<Record<string, (script: Script) => boolean>> = { all: () => true, on: (script: Script) => script.on }; FILTERS[tab]',
    // A mutable list and a list inside a function keep their annotation.
    'const names: string[] = [\'a\']; names.push(\'b\')',
    'const events: readonly string[] = [\'a\', \'b\']; declare const name: string; events.includes(name)',
    'type Group = { entries: readonly string[] }; const groups: readonly Group[] = [{ entries: [\'a\'] }]',
    'function run() { const rows: readonly number[] = [1, 2]; return rows }',
    // Callbacks: a parameter annotation that widens the contextual type stays.
    'declare const rows: number[]; rows.map((row: number | string) => String(row))',
    // A reduce accumulator feeds the inference of the result type.
    'declare const rows: number[]; rows.reduce((total: number[], row) => total, [])',
    // A callback outside an array method is left alone.
    'declare function run(cb: (n: number) => { a: number }): void; run((n): { a: number } => ({ a: n }))',
    // An async callback returns a promise, not the literal.
    'declare const rows: number[]; rows.map(async (row): Promise<{ a: number }> => ({ a: row }))',
  ],
  invalid: [
    {
      code: 'type Theme = { bg: string; fg: string }; const dark: Theme = { bg: \'#000\', fg: \'#fff\' }',
      output:
        'type Theme = { bg: string; fg: string }; const dark = { bg: \'#000\', fg: \'#fff\' } as const satisfies Theme',
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
      code: 'type Preset = \'calm\' | \'loud\'; const volume: Record<Preset, number> = { calm: 1, loud: 9 }',
      output:
        'type Preset = \'calm\' | \'loud\'; const volume = { calm: 1, loud: 9 } as const satisfies Record<Preset, number>',
      errors: [{ messageId: 'table' }],
    },
    {
      code: 'const sizes: Record<string, { px: number }> = { sm: { px: 14 } }; for (const [name, size] of Object.entries(sizes)) console.log(name, size.px, sizes.sm, sizes[\'sm\'])',
      output:
        'const sizes = { sm: { px: 14 } } as const satisfies Record<string, { px: number }>; for (const [name, size] of Object.entries(sizes)) console.log(name, size.px, sizes.sm, sizes[\'sm\'])',
      errors: [{ messageId: 'table' }],
    },
    {
      code: 'type Script = { on: boolean }; const FILTERS: Readonly<Record<string, (script: Script) => boolean>> = { all: () => true, on: (script: Script) => script.on }; FILTERS.all',
      output:
        'type Script = { on: boolean }; const FILTERS = { all: () => true, on: (script: Script) => script.on } as const satisfies Readonly<Record<string, (script: Script) => boolean>>; FILTERS.all',
      errors: [{ messageId: 'table' }],
    },
    {
      code: 'type Route = { name: string }; const likelyNext: readonly Route[] = [{ name: \'chat\' }, { name: \'people\' }]',
      output:
        'type Route = { name: string }; const likelyNext = [{ name: \'chat\' }, { name: \'people\' }] as const satisfies readonly Route[]',
      errors: [{ messageId: 'list' }],
    },
    {
      code: 'export const steps: ReadonlyArray<{ id: number }> = [{ id: 1 }]',
      output: 'export const steps = [{ id: 1 }] as const satisfies ReadonlyArray<{ id: number }>',
      errors: [{ messageId: 'list' }],
    },
    {
      // useCurrentChannel: the return type moves from the arrow to the literal.
      code: 'type Choice = { kind: \'own\' | \'other\'; id: string }; declare const rows: { id: string }[]; rows.map((row): Choice => ({ kind: \'own\', id: row.id }))',
      output:
        'type Choice = { kind: \'own\' | \'other\'; id: string }; declare const rows: { id: string }[]; rows.map((row) => ({ kind: \'own\', id: row.id }) as const satisfies Choice)',
      errors: [{ messageId: 'callbackReturn' }],
    },
    {
      // OverlaysView: a generic return type, the argument is typed by the list.
      code: 'type PageTab<T> = { value: T; label: string }; type Tab = \'a\' | \'b\'; declare const TAB_VALUES: readonly Tab[]; TAB_VALUES.map((value): PageTab<Tab> => ({ value, label: value }))',
      output:
        'type PageTab<T> = { value: T; label: string }; type Tab = \'a\' | \'b\'; declare const TAB_VALUES: readonly Tab[]; TAB_VALUES.map((value) => ({ value, label: value }) as const satisfies PageTab<Tab>)',
      errors: [{ messageId: 'callbackReturn' }],
    },
    {
      // OverlaysView: the list is a non-empty tuple and the row has an optional count.
      code: 'interface PageTab<V extends string = string> { value: V; label: string; count?: number }; declare const TAB_VALUES: [\'a\', ...\'a\'[]]; TAB_VALUES.map((value): PageTab<\'a\'> => ({ value, label: value }))',
      output: null,
      errors: [{ messageId: 'callbackReturn' }],
    },
    {
      // A block body puts it on each returned literal.
      code: 'type Row = { id: number }; declare const ids: number[]; ids.flatMap((id): readonly Row[] => { if (id > 1) { return [{ id }] } return [] })',
      output:
        'type Row = { id: number }; declare const ids: number[]; ids.flatMap((id) => { if (id > 1) { return [{ id }] as const satisfies readonly Row[] } return [] as const satisfies readonly Row[] })',
      errors: [{ messageId: 'callbackReturn' }],
    },
    {
      // One return is not a literal: report, no fix.
      code: 'type Row = { id: number }; declare const ids: number[]; declare const fallback: Row; ids.map((id): Row => { if (id > 1) { return { id } } return fallback })',
      output: null,
      errors: [{ messageId: 'callbackReturn' }],
    },
    {
      // A mutable list inside the row: `as const` would freeze it, so report, no fix.
      code: 'type Row = { ids: number[] }; declare const rows: number[]; rows.map((row): Row => ({ ids: [row] }))',
      output: null,
      errors: [{ messageId: 'callbackReturn' }],
    },
    {
      // A shape `as const` would loosen (optional field): report, no fix.
      code: 'type Row = { id: number; note?: string }; declare const ids: number[]; ids.map((id): Row => ({ id }))',
      output: null,
      errors: [{ messageId: 'callbackReturn' }],
    },
    {
      code: 'declare const rows: { id: number }[]; rows.map((row: { id: number }) => row.id)',
      output: 'declare const rows: { id: number }[]; rows.map((row) => row.id)',
      errors: [{ messageId: 'callbackParam' }],
    },
    {
      code: 'declare const rows: { id: number }[]; rows.filter(({ id }: { id: number }, index: number) => id > index)',
      output: 'declare const rows: { id: number }[]; rows.filter(({ id }, index) => id > index)',
      errors: [{ messageId: 'callbackParam' }, { messageId: 'callbackParam' }],
    },
    {
      // The reduce row (second parameter) is typed by the list; the accumulator is not touched.
      code: 'declare const rows: number[]; rows.reduce((total, row: number) => total + row, 0)',
      output: 'declare const rows: number[]; rows.reduce((total, row) => total + row, 0)',
      errors: [{ messageId: 'callbackParam' }],
    },
  ],
});
