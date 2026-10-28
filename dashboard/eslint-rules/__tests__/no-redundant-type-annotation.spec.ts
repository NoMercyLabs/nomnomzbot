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
    // Widens: an empty list, an index over a bare object.
    'const ids: string[] = []',
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
    // An open table handed on in an options object keeps its index: the consumer infers its keys from it.
    'declare function open<T>(messages: T): T; const start: Record<string, number> = { en: 1 }; open({ messages: start })',
    // `as const` would reject the write or freeze the list.
    'const counter: { seen: number } = { seen: 0 }; counter.seen++',
    'const bucket: { seen: string[] } = { seen: [] }',
    // A mutable list and a list inside a function keep their annotation.
    'const names: string[] = [\'a\']; names.push(\'b\')',
    'type Group = { entries: readonly string[] }; const groups: readonly Group[] = [{ entries: [\'a\'] }]',
    'function run() { const rows: readonly number[] = [1, 2]; return rows }',
    // Callbacks: a parameter annotation that widens the contextual type stays.
    'declare const rows: number[]; rows.map((row: number | string) => String(row))',
    // A reduce accumulator feeds the inference of the result type.
    'declare const rows: number[]; rows.reduce((total: number[], row) => total, [])',
    // A recursive function needs its return type: TS cannot infer it.
    'type Node = { next: Node | null }; function walk(depth: number): Node { return depth > 0 ? walk(depth - 1) : { next: null } }',
    'type Tree = { kids: Tree[] }; const grow = (depth: number): Tree => { if (depth > 0) return grow(depth - 1); return { kids: [] } }',
    // An overload keeps its annotation.
    'type Box = { a: number }; function make(a: number): Box; function make(a: string): Box; function make(a: number | string): Box { return { a: Number(a) } }',
    // A returned value that is no literal already has its type.
    'type Box = { a: number }; declare const box: Box; function pick(): Box { return box }',
    // An async function returns a promise, not the literal.
    'type Box = { a: number }; async function load(): Promise<Box> { return { a: 1 } }',
    // A nullable union stays: the `person is Box` filter that reads the list needs the exact union.
    'type Box = { a: number }; function maybe(on: boolean): Box | null { if (!on) return null; return { a: 1 } }',
    // A type predicate and a loose return type stay.
    'declare function make(): unknown; function made(): unknown { return { a: 1 } }',
    // An async callback returns a promise, not the literal.
    'declare const rows: number[]; rows.map(async (row): Promise<{ a: number }> => ({ a: row }))',
    // A mutable list inside would freeze under `as const`: the annotation stays, never a bare satisfies.
    'type Box = { ids: number[] }; function make(): Box { return { ids: [1] } }',
    // A literal with no target type (no generic, no annotation) stays as it is.
    'declare function computed<T>(getter: () => T): { value: T }; const context = computed(() => ({ a: 1 }))',
    // A generic that is not the callback result stays: ref<T> takes a value, not a getter.
    'declare function lazy<T>(value: T, getter: () => number): { value: T }; const x = lazy<{ a: number }>({ a: 1 }, () => 1)',
    // A string field `as const` would narrow keeps its generic.
    'type Form = { name: string }; declare function computed<T>(getter: () => T): { value: T }; const form = computed<Form>(() => ({ name: \'\' }))',
    // One getter return that is no literal keeps the generic whole (useBoard inline).
    'declare const flag: boolean; declare const rest: string[]; declare function computed<T>(getter: () => T): { value: T }; const inline = computed<readonly string[]>(() => { if (flag) return []; return rest })',
    // A string-keyed record return (accentTokens) keeps its annotation: two returns infer two shapes.
    'function tokens(on: boolean): Record<string, string> { if (!on) return {}; return { a: \'x\' } }',
    // A form blank state: `as const` would narrow the boolean to false and reject every later write.
    'type Form = { open: boolean }; function blank(): Form { return { open: false } }',
    // A form blank state: `as const` would narrow the string to \'\'.
    'type Form = { name: string }; function blank(): Form { return { name: \'\' } }',
  ],
  invalid: [
    {
      // emotePicker: a function declaration states its type on the returned literal.
      code: 'type Section = { id: string; items: readonly string[] }; function section(id: string): Section { return { id, items: [\'a\'] } }',
      output:
        'type Section = { id: string; items: readonly string[] }; function section(id: string) { return { id, items: [\'a\'] } as const satisfies Section }',
      errors: [{ messageId: 'callbackReturn' }],
    },
    {
      // An arrow that is no array callback does the same.
      code: 'type Box = { a: number }; const box = (a: number): Box => ({ a })',
      output: 'type Box = { a: number }; const box = (a: number) => ({ a }) as const satisfies Box',
      errors: [{ messageId: 'callbackReturn' }],
    },
    {
      // Every returned literal moves; a returned variable stays as it is.
      code: 'type Box = { a: number }; declare const none: Box | null; function pick(a: number): Box | null { if (a > 1) return { a }; return none }',
      output:
        'type Box = { a: number }; declare const none: Box | null; function pick(a: number) { if (a > 1) return { a } as const satisfies Box; return none }',
      errors: [{ messageId: 'callbackReturn' }],
    },
    {
      // A class method counts too.
      code: 'type Box = { a: number }; class Maker { make(a: number): Box { return { a } } }',
      output: 'type Box = { a: number }; class Maker { make(a: number) { return { a } as const satisfies Box } }',
      errors: [{ messageId: 'callbackReturn' }],
    },
    {
      // EmotePicker: the generic moves to the returned literal.
      code: 'type Ctx = { a: number; b: readonly string[] }; declare const n: number; declare const list: string[]; declare function computed<T>(getter: () => T): { value: T }; const context = computed<Ctx>(() => ({ a: n, b: list }))',
      output:
        'type Ctx = { a: number; b: readonly string[] }; declare const n: number; declare const list: string[]; declare function computed<T>(getter: () => T): { value: T }; const context = computed(() => ({ a: n, b: list }) as const satisfies Ctx)',
      errors: [{ messageId: 'callbackTarget' }],
    },
    {
      // The same through the variable annotation.
      code: 'type Ctx = { a: number }; declare const n: number; type Box<T> = { value: T }; declare function computed<T>(getter: () => T): Box<T>; const context: Box<Ctx> = computed(() => ({ a: n }))',
      output:
        'type Ctx = { a: number }; declare const n: number; type Box<T> = { value: T }; declare function computed<T>(getter: () => T): Box<T>; const context = computed(() => ({ a: n }) as const satisfies Ctx)',
      errors: [{ messageId: 'callbackTarget' }],
    },
    {
      // A block body states it on each returned literal.
      code: 'type Ctx = { a: number }; declare const n: number; declare function computed<T>(getter: () => T): { value: T }; const context = computed<Ctx>(() => { return { a: n } })',
      output:
        'type Ctx = { a: number }; declare const n: number; declare function computed<T>(getter: () => T): { value: T }; const context = computed(() => { return { a: n } as const satisfies Ctx })',
      errors: [{ messageId: 'callbackTarget' }],
    },
    {
      // OverlayDetail: every return of the getter, early ifs included, states the type.
      code: 'type Line = { tone: \'ok\' | \'off\' }; declare const flag: boolean; declare function computed<T>(getter: () => T): { value: T }; const line = computed<Line>(() => { if (flag) return { tone: \'off\' }; return { tone: \'ok\' } })',
      output:
        'type Line = { tone: \'ok\' | \'off\' }; declare const flag: boolean; declare function computed<T>(getter: () => T): { value: T }; const line = computed(() => { if (flag) return { tone: \'off\' } as const satisfies Line; return { tone: \'ok\' } as const satisfies Line })',
      errors: [{ messageId: 'callbackTarget' }],
    },
    {
      // EmotePicker starrable: a literal is never null, so the empty member leaves the satisfies type.
      code: 'type Star = { id: string }; declare const id: string; declare const flag: boolean; function starrable(): Star | null { if (flag) return { id }; return flag ? { id } : null }',
      output:
        'type Star = { id: string }; declare const id: string; declare const flag: boolean; function starrable() { if (flag) return { id } as const satisfies Star; return flag ? { id } as const satisfies Star : null }',
      errors: [{ messageId: 'callbackReturn' }],
    },
    {
      // A `satisfies` already written with the empty member loses it.
      code: 'type Star = { id: string }; declare const id: string; const star = { id } as const satisfies Star | null',
      output: 'type Star = { id: string }; declare const id: string; const star = { id } as const satisfies Star',
      errors: [{ messageId: 'literalNeverNull' }],
    },
    {
      // A bare `satisfies` on a literal gains `as const`: the query key stays a read-only tuple.
      code: 'type KeyParts = readonly unknown[]; export const key = (id: string | null) => [\'channels\', id, \'access\'] satisfies KeyParts',
      output: 'type KeyParts = readonly unknown[]; export const key = (id: string | null) => [\'channels\', id, \'access\'] as const satisfies KeyParts',
      errors: [{ messageId: 'bareSatisfies' }],
    },
    {
      // A string-keyed table read by a computed key still moves to `as const satisfies`; the read narrows the key.
      code: 'declare const name: string; const scores: Record<string, number> = { a: 1 }; scores[name]',
      output:
        'declare const name: string; const scores = { a: 1 } as const satisfies Record<string, number>; scores[name]',
      errors: [{ messageId: 'table' }],
    },
    {
      code: 'export const shared: Record<string, number> = { a: 1 }',
      output: 'export const shared = { a: 1 } as const satisfies Record<string, number>',
      errors: [{ messageId: 'table' }],
    },
    {
      code: 'const events: readonly string[] = [\'a\', \'b\']',
      output: 'const events = [\'a\', \'b\'] as const satisfies readonly string[]',
      errors: [{ messageId: 'list' }],
    },
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
      output:
        'interface PageTab<V extends string = string> { value: V; label: string; count?: number }; declare const TAB_VALUES: [\'a\', ...\'a\'[]]; TAB_VALUES.map((value) => ({ value, label: value }) as const satisfies PageTab<\'a\'>)',
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
      // A return that is no literal keeps its own type; the literal states its type.
      code: 'type Row = { id: number }; declare const ids: number[]; declare const fallback: Row; ids.map((id): Row => { if (id > 1) { return { id } } return fallback })',
      output:
        'type Row = { id: number }; declare const ids: number[]; declare const fallback: Row; ids.map((id) => { if (id > 1) { return { id } as const satisfies Row } return fallback })',
      errors: [{ messageId: 'callbackReturn' }],
    },
    {
      // A spread brings the optional field along, so nothing is dropped (music.ts renumbered).
      code: 'type Row = { id: number; note?: string }; declare const rows: Row[]; rows.map((row, index): Row => ({ ...row, id: index }))',
      output:
        'type Row = { id: number; note?: string }; declare const rows: Row[]; rows.map((row, index) => ({ ...row, id: index }) as const satisfies Row)',
      errors: [{ messageId: 'callbackReturn' }],
    },
    {
      // A tuple is frozen too: the satisfies type is readonly.
      code: 'declare const ids: number[]; ids.map((id): [number, string] => [id, String(id)])',
      output:
        'declare const ids: number[]; ids.map((id) => [id, String(id)] as const satisfies readonly [number, string])',
      errors: [{ messageId: 'callbackReturn' }],
    },
    {
      // A list read from elsewhere is not frozen: only a list the literal writes out is.
      code: 'type Row = { ids: number[] }; declare const rows: Row[]; rows.map((row): Row => ({ ids: row.ids }))',
      output:
        'type Row = { ids: number[] }; declare const rows: Row[]; rows.map((row) => ({ ids: row.ids }) as const satisfies Row)',
      errors: [{ messageId: 'callbackReturn' }],
    },
    {
      // A mutable list inside the row: `as const` would freeze it, so report, no fix.
      code: 'type Row = { ids: number[] }; declare const rows: number[]; rows.map((row): Row => ({ ids: [row] }))',
      output: null,
      errors: [{ messageId: 'callbackReturn' }],
    },
    {
      // An optional field the literal leaves out stays out; the type check names a read of it.
      code: 'type Row = { id: number; note?: string }; declare const ids: number[]; ids.map((id): Row => ({ id }))',
      output:
        'type Row = { id: number; note?: string }; declare const ids: number[]; ids.map((id) => ({ id }) as const satisfies Row)',
      errors: [{ messageId: 'callbackReturn' }],
    },
    {
      // flatMap reads a readonly list: the mutable list return turns readonly (voiceGroups).
      code: 'type Item = { key: string }; declare const ids: string[]; ids.flatMap((id): Item[] => [{ key: id }, { key: id + \'x\' }])',
      output:
        'type Item = { key: string }; declare const ids: string[]; ids.flatMap((id) => [{ key: id }, { key: id + \'x\' }] as const satisfies readonly Item[])',
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
