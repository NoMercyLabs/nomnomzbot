// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createPinia, setActivePinia, type Pinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ref } from 'vue'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'

import { i18n } from '@/i18n'

import ScriptRunsPanel from '../components/ScriptRunsPanel.vue'
import type { SessionRun } from '../components/sessionRun'
import ScriptsView from '../ScriptsView.vue'
import en from '../i18n/en.json'

type Options = {
  params?: { path?: Record<string, unknown>; query?: Record<string, unknown> }
  body?: unknown
}
type Call = {
  method: string
  path: string
  body: unknown
  id: string
}
type Answer = {
  data?: unknown
  error?: unknown
  response: {
    status: number
    ok: boolean
    headers?: Headers
  }
}

const REFUSAL: string = 'Custom code is switched off for this channel.'

const WORKED: Record<string, unknown> = {
  success: true,
  error: null,
  durationMs: 42,
  hostCallCount: 1,
  capturedEffects: [],
  chatOutput: ['hello'],
  log: [],
  variablesSet: null,
  console: null,
  timeline: [{ seq: 1, kind: 'start', text: 'Started: chat message' }],
}
const BROKE: Record<string, unknown> = {
  ...WORKED,
  success: false,
  error: 'ReferenceError: raider is not defined',
  durationMs: 7,
  timeline: [
    { seq: 1, kind: 'start', text: 'Started: chat message' },
    { seq: 2, kind: 'error', text: 'Stopped with an error' },
  ],
  capturedEffects: [{ name: 'chat.send', argsPreview: '"hi"' }],
  errorPosition: { file: 'index.ts', line: 4, column: 42 },
}

const calls: Call[] = []
let testResult: Record<string, unknown> = WORKED
let testLimited: boolean = false
let scripts: Record<string, unknown>[] = []
let refused: boolean = false
let blast: Record<string, unknown> = { categories: [], isMinimum: false, totalReferences: 0 }
let detailExtra: Record<string, unknown> = {}

const script = (
  id: string,
  name: string,
  over: Record<string, unknown> = {},
): Record<string, unknown> => ({
  id,
  name,
  description: `${name} description`,
  isEnabled: true,
  currentVersion: 1,
  currentValidationStatus: 'valid',
  lastRuntimeError: null,
  lastRanAt: '2026-10-12T20:00:00Z',
  createdAt: '2026-01-01T10:00:00Z',
  updatedAt: '2026-01-01T10:00:00Z',
  ...over,
})

const fixtures = (): Record<string, unknown>[] => [
  script('s1', 'Hype Train Banner'),
  script('s2', 'Raid Welcome', {
    lastRuntimeError: 'ReferenceError: raider is not defined',
  }),
  script('s3', 'Quote Keeper', { isEnabled: false, lastRanAt: null }),
  script('s4', 'Clip Poster', { isEnabled: false, description: 'Posts new clips' }),
]

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string, options?: Options): Promise<Answer> => {
      const id = String(options?.params?.path?.id ?? '')

      calls.push({ method: 'GET', path, body: undefined, id })

      if (refused)
        return {
          error: { message: REFUSAL },
          response: { status: 403, ok: false },
        }

      if (path.endsWith('/test-triggers'))
        return {
          data: {
            data: [
              {
                id: 'tr1',
                responseKey: 'channel.follow',
                userDisplayName: 'sample_viewer',
                variables: { user: 'sample_viewer' },
              },
            ],
          },
          response: { status: 200, ok: true },
        }

      if (path.endsWith('/blast-radius'))
        return { data: { data: blast }, response: { status: 200, ok: true } }

      if (path.endsWith('/code-scripts/{id}')) {
        const row = scripts.find((one) => one.id === id)

        return { data: { data: { ...row, ...detailExtra } }, response: { status: 200, ok: true } }
      }

      return { data: { data: scripts }, response: { status: 200, ok: true } }
    },
    POST: async (path: string, options?: Options): Promise<Answer> => {
      const id = String(options?.params?.path?.id ?? '')

      calls.push({ method: 'POST', path, body: options?.body, id })

      if (path.endsWith('/test-run')) {
        if (testLimited)
          return {
            error: { message: 'Too many tests' },
            response: { status: 429, ok: false, headers: new Headers({ 'Retry-After': '30' }) },
          }

        return {
          data: { data: testResult },
          response: { status: 200, ok: true, headers: new Headers() },
        }
      }

      return { data: { data: { id: 'new1' } }, response: { status: 200, ok: true } }
    },
    DELETE: async (path: string, options?: Options): Promise<Answer> => {
      const id = String(options?.params?.path?.id)

      calls.push({ method: 'DELETE', path, body: undefined, id })
      scripts = scripts.filter((row) => row.id !== id)

      return { data: { data: null }, response: { status: 200, ok: true } }
    },
    PATCH: async (path: string, options?: Options): Promise<Answer> => {
      const id = String(options?.params?.path?.id)

      calls.push({ method: 'PATCH', path, body: options?.body, id })
      const body = options?.body as Record<string, unknown>

      scripts = scripts.map((row) =>
        row.id === id
          ? { ...row, ...body }
          : row)

      return { data: { data: null }, response: { status: 200, ok: true } }
    },
  },
}))

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}))

let mounted: VueWrapper | null = null
let pinia: Pinia
let router: Router

beforeEach(() => {
  localStorage.clear()
  calls.length = 0
  refused = false
  testResult = WORKED
  testLimited = false

  blast = {
    categories: [
      { categoryKey: 'dashboard.scripts.blast.pipelines', count: 2, sample: ['Raid flow', 'Hype'] },
      { categoryKey: 'dashboard.scripts.blast.timers', count: 1, sample: ['Hourly'] },
    ],
    isMinimum: false,
    totalReferences: 3,
  }

  detailExtra = {
    language: 'typescript',
    currentVersionId: 'v1',
    currentVersion: {
      version: 3,
      declaredCapabilities: ['chat.send', 'variables.write'],
    },
  }

  scripts = fixtures()
  pinia = createPinia()
  setActivePinia(pinia)
  i18n.global.mergeLocaleMessage('en', en)
})

afterEach(() => {
  mounted?.unmount()
  mounted = null
  document.body.innerHTML = ''
  vi.restoreAllMocks()
})

const settle = async (): Promise<void> => {
  await flushPromises()
  await new Promise((done) => setTimeout(done, 0))
  await flushPromises()
}

async function open(url: string = '/app/scripts'): Promise<void> {
  const blank = { template: '<div />' }

  router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/app/scripts', name: 'dashboard-scripts', component: blank }],
  })

  await router.push(url)

  mounted = mount(ScriptsView, {
    attachTo: document.body,
    global: {
      plugins: [
        i18n,
        pinia,
        router,
        [
          VueQueryPlugin,
          { queryClient: new QueryClient({ defaultOptions: { queries: { retry: false } } }) },
        ],
      ],
    },
  })

  await settle()
}

const text = (): string => document.body.textContent ?? ''
const rows = (): HTMLElement[] => [
  ...document.body.querySelectorAll<HTMLElement>('[data-script-row]'),
]
const rowNames = (): string[] =>
  rows().map((row) => row.querySelector('span.font-medium')?.textContent?.trim() ?? '')
const tabCount = (label: string): string =>
  (
    [...document.body.querySelectorAll<HTMLElement>('[role="tab"]')].find((t) =>
      t.textContent?.trim().startsWith(label),
    )?.textContent ?? ''
  )
    .replace(label, '')
    .trim()
const byText = (selector: string, label: string): HTMLElement | undefined =>
  [...document.body.querySelectorAll<HTMLElement>(selector)].find(
    (node) => node.textContent?.trim() === label,
  )

const typeSearch = async (value: string): Promise<void> => {
  const input = document.body.querySelector('input[type="search"]') as HTMLInputElement

  input.value = value
  input.dispatchEvent(new Event('input', { bubbles: true }))
  await settle()
}

describe('ScriptsView', () => {
  it('counts each tab from the list, with Failed as the rows that have a runtime error', async () => {
    await open()

    expect(tabCount('All')).toBe('4')
    expect(tabCount('On')).toBe('2')
    expect(tabCount('Off')).toBe('2')
    expect(tabCount('Failed')).toBe('1')
    expect(rowNames()).toEqual(['Hype Train Banner', 'Raid Welcome', 'Quote Keeper', 'Clip Poster'])

    await router.push('/app/scripts?view=failed')
    await settle()
    expect(rowNames()).toEqual(['Raid Welcome'])

    await router.push('/app/scripts?view=off')
    await settle()
    expect(rowNames()).toEqual(['Quote Keeper', 'Clip Poster'])
  })

  it('filters the list by the name or description typed in the search box', async () => {
    await open()

    await typeSearch('raid')
    expect(rowNames()).toEqual(['Raid Welcome'])

    await typeSearch('new clips')
    expect(rowNames()).toEqual(['Clip Poster'])

    await typeSearch('zzz')
    expect(rowNames()).toEqual([])
    expect(text()).toContain('No script matches "zzz".')
  })

  it('switches a script off with a PATCH of isEnabled only and moves the tab counts', async () => {
    await open('/app/scripts?view=on')
    const toggle = rows()[0]!.querySelector('button[role="switch"]') as HTMLButtonElement

    expect(toggle.getAttribute('aria-checked')).toBe('true')

    toggle.click()
    await settle()

    const patches = calls.filter((call) => call.method === 'PATCH')

    expect(patches).toHaveLength(1)
    expect(patches[0]!.path).toContain('/code-scripts/{id}/enabled')
    expect(patches[0]!.id).toBe('s1')
    expect(patches[0]!.body).toEqual({ isEnabled: false })
    expect(tabCount('On')).toBe('1')
    expect(tabCount('Off')).toBe('3')
    expect(rowNames()).toEqual(['Raid Welcome'])
  })

  it('says Never run when a script has no last run, and Last run when it has', async () => {
    await open()

    expect(rows()[2]!.textContent).toContain('Never run')
    expect(rows()[0]!.textContent).toContain('Last run')
    expect(rows()[0]!.textContent).not.toContain('Never run')
    expect(rows()[1]!.textContent).toContain('Failed')
  })

  it('shows the bot reason, not the empty text, when the bot refuses the list', async () => {
    refused = true
    await open()

    expect(text()).toContain(REFUSAL)
    expect(text()).not.toContain('This channel has no scripts yet.')
    expect(rowNames()).toEqual([])
  })

  it('opens Delete with the blast counts, sends no DELETE before confirm and one after', async () => {
    await open('/app/scripts?script=s1')
    expect(calls.filter((call) => call.method === 'DELETE')).toHaveLength(0)

    byText('button', 'Delete')!.click()
    await settle()

    const dialog: string = document.body.querySelector('[role="alertdialog"]')?.textContent ?? ''

    expect(dialog).toContain('2 pipelines')
    expect(dialog).toContain('1 timers')

    expect(calls.filter((call) => call.method === 'DELETE')).toHaveLength(0)

    ;(document.body.querySelector('[data-confirm-action]') as HTMLElement).click()
    await settle()

    const deletes = calls.filter((call) => call.method === 'DELETE')

    expect(deletes).toHaveLength(1)
    expect(deletes[0]!.id).toBe('s1')
    expect(rowNames()).not.toContain('Hype Train Banner')
  })

  it('says at least when the blast count is only a minimum', async () => {
    blast = { ...blast, isMinimum: true }
    await open('/app/scripts?script=s1')

    byText('button', 'Delete')!.click()
    await settle()

    const dialog: string = document.body.querySelector('[role="alertdialog"]')?.textContent ?? ''

    expect(dialog).toContain('At least 2 pipelines')
    expect(dialog).toContain('At least 1 timers')
  })

  it('creates a script with name, description and starter code, then opens it', async () => {
    await open()

    byText('button', 'New script')!.click()
    await settle()
    const input = document.body.querySelector('[role="dialog"] input') as HTMLInputElement

    input.value = 'Dice'
    input.dispatchEvent(new Event('input', { bubbles: true }))
    await settle()
    byText('button', 'Create script')!.click()
    await settle()

    const posts = calls.filter((call) => call.method === 'POST')

    expect(posts).toHaveLength(1)
    const body = posts[0]!.body as Record<string, unknown>

    expect(body.name).toBe('Dice')
    expect(body.description).toBe('')
    expect(String(body.sourceCode)).toContain('nnz.random.int')
    expect(router.currentRoute.value.query.script).toBe('new1')
  })

  it('lists the declared powers as plain text with no control', async () => {
    await open('/app/scripts?script=s1')

    const powers = document.body.querySelector('[data-slot="script-powers"]') as HTMLElement

    expect(powers.textContent).toContain('chat.send')
    expect(powers.textContent).toContain('variables.write')
    expect(powers.querySelectorAll('button, input, [role="switch"]')).toHaveLength(0)
    expect(document.body.textContent).toContain('Version')
    expect(document.body.textContent).toContain('typescript')
  })

  it('Run test posts to the test-run path of the script and shows the result with its duration', async () => {
    await open('/app/scripts?script=s1')

    byText('button', 'Run test')!.click()
    await settle()

    const posts = calls.filter((call) => call.method === 'POST')

    expect(posts).toHaveLength(1)
    expect(posts[0]!.path).toBe('/api/v{version}/code-scripts/{id}/test-run')
    expect(posts[0]!.id).toBe('s1')
    expect(posts[0]!.body).toEqual({ variables: {}, args: [], trigger: null })
    const result: string =
      document.body.querySelector('[data-slot="script-test-result"]')?.textContent ?? ''

    expect(result).toContain('Worked')
    expect(result).toContain('42 ms')
    expect(document.body.querySelector('[role="dialog"]')).toBeNull()
  })

  it('a per-trigger test sends that trigger and its sample variables', async () => {
    await open('/app/scripts?script=s1')

    byText('button', 'Test')!.click()
    await settle()

    const posts = calls.filter((call) => call.method === 'POST')

    expect(posts).toHaveLength(1)

    expect(posts[0]!.body).toEqual({
      variables: { user: 'sample_viewer' },
      args: [],
      trigger: 'tr1',
    })
  })

  it('a failed result opens the dialog with the error, the line and column and the steps', async () => {
    testResult = BROKE
    await open('/app/scripts?script=s2')

    byText('button', 'Run test')!.click()
    await settle()

    const dialog: string = document.body.querySelector('[role="dialog"]')?.textContent ?? ''

    expect(dialog).toContain('The run failed')
    expect(dialog).toContain('ReferenceError: raider is not defined')
    expect(dialog).toContain('index.ts line 4, column 42')
    expect(dialog).toContain('Stopped with an error')
    expect(dialog).toContain('chat.send')
  })

  it('lists session runs newest first and the Failed filter hides the one that worked', async () => {
    await open('/app/scripts?script=s1')
    byText('button', 'Run test')!.click()
    await settle()

    testResult = BROKE
    await router.push('/app/scripts?script=s2')
    await settle()
    byText('button', 'Run test')!.click()

    await settle()

    ;(document.body.querySelector('[data-close]') as HTMLElement).click()
    await settle()

    const runNames = (): string[] =>
      [...document.body.querySelectorAll<HTMLElement>('[data-run-row] [data-run-name]')].map(
        (node) => node.textContent?.trim() ?? '',
      )

    expect(runNames()).toEqual(['Raid Welcome', 'Hype Train Banner'])

    byText('[data-slot="toggle-group-item"]', 'Failed')!.click()
    await settle()
    expect(runNames()).toEqual(['Raid Welcome'])
  })

  it('keeps the run filter after the runs panel is opened again', async () => {
    const run = (id: number, name: string, success: boolean): SessionRun => ({
      id,
      scriptId: `s${id}`,
      scriptName: name,
      at: new Date(2026, 9, 13, 12, id),
      trigger: null,
      result: { ...(WORKED as object), success } as SessionRun['result'],
    })
    const runs: SessionRun[] = [run(1, 'Raid Welcome', true), run(2, 'Hype Train Banner', false)]
    const names = (): string[] =>
      [...document.body.querySelectorAll<HTMLElement>('[data-run-row] [data-run-name]')].map(
        (node) => node.textContent?.trim() ?? '',
      )
    const panel = (): VueWrapper =>
      mount(ScriptRunsPanel, {
        attachTo: document.body,
        props: { runs },
        global: { plugins: [i18n] },
      })

    const first = panel()

    byText('[data-slot="toggle-group-item"]', 'Failed')!.click()
    await settle()
    expect(names()).toEqual(['Hype Train Banner'])
    first.unmount()
    document.body.innerHTML = ''

    const second = panel()

    await settle()
    expect(names()).toEqual(['Hype Train Banner'])
    second.unmount()
  })

  it('a 429 disables Run test and says to wait', async () => {
    testLimited = true
    await open('/app/scripts?script=s1')

    byText('button', 'Run test')!.click()
    await settle()

    expect((byText('button', 'Run test') as HTMLButtonElement).disabled).toBe(true)
    expect(text()).toContain('Too many tests, wait a moment')
    expect(document.body.querySelector('[data-run-row]')).toBeNull()
  })

  it('says so when the channel has no scripts', async () => {
    scripts = []
    await open()

    expect(text()).toContain('This channel has no scripts yet.')
  })
})
