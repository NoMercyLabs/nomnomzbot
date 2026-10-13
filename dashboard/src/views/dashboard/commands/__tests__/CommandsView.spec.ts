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

import { permissionLevels } from '../components/permissionLevels'
import CommandsView from '../CommandsView.vue'
import en from '../i18n/en.json'

type Options = { params?: { path?: Record<string, unknown> }; body?: Record<string, unknown> }
type Call = {
  method: string
  path: string
  name: unknown
  body: Record<string, unknown> | undefined
}
type Answer = { data?: unknown; error?: unknown; response: { status: number; ok: boolean } }
type Row = Record<string, unknown>

const calls: Call[] = []
let answers: Record<string, () => Answer> = {}
let rows: Row[] = []
let refusePut: string | null = null
let refusePost: string | null = null

const ok = (data: unknown): Answer => ({ data: { data }, response: { status: 200, ok: true } })
const refused = (message: string, status: number = 400): Answer => ({
  error: { message },
  response: { status, ok: false },
})

const command = (name: string, overrides: Row = {}): Row => ({
  id: `id-${name}`,
  name,
  tier: 'template',
  minPermissionLevel: 'Everyone',
  isEnabled: true,
  prefixMode: 'Default',
  customPrefix: null,
  matchMode: 'StartsWith',
  matchPattern: null,
  cooldownSeconds: 5,
  userCooldownSeconds: 10,
  cooldownPerUser: false,
  description: null,
  aliases: [],
  useCount: 0,
  createdAt: '2026-10-01T10:00:00Z',
  templateResponse: 'Hello there',
  templateResponses: null,
  pipelineId: null,
  presetKey: null,
  speakWithTts: false,
  ...overrides,
})

function freshAnswers(): Record<string, () => Answer> {
  return {
    '/commands': () => ok(rows.map((row) => ({ ...row }))),
    '/pipelines': () => ok([{ id: 'p1', name: 'Welcome flow', isEnabled: true }]),
    '/code-scripts': () => ok([]),
    '/templates/helpers': () => ok([]),
    '/builtins': () =>
      ok([
        {
          builtinKey: 'uptime',
          name: 'uptime',
          isEnabled: true,
          isReserved: false,
          speakWithTts: false,
          replyOverrideCount: 0,
        },
      ]),
  }
}

function read(path: string): Answer {
  const match: string | undefined = Object.keys(answers).find((suffix) => path.endsWith(suffix))
  return match ? answers[match]!() : ok(null)
}

function write(method: string, path: string, options?: Options): Answer {
  calls.push({
    method,
    path,
    name: options?.params?.path?.commandName ?? options?.params?.path?.builtinKey,
    body: options?.body,
  })
  if (method === 'PUT' && refusePut) return refused(refusePut)
  if (method === 'POST' && refusePost) return refused(refusePost, 409)
  if (method === 'PUT') {
    const index: number = rows.findIndex((row) => row.name === options?.params?.path?.commandName)
    rows[index] = { ...rows[index], ...options?.body }
  }
  if (method === 'DELETE')
    rows = rows.filter((row) => row.name !== options?.params?.path?.commandName)
  if (method === 'POST') rows.push(command(String(options?.body?.name), options?.body))
  return { response: { status: 200, ok: true } }
}

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string) => read(path),
    POST: async (path: string, options?: Options) => write('POST', path, options),
    PUT: async (path: string, options?: Options) => write('PUT', path, options),
    PATCH: async (path: string, options?: Options) => write('PATCH', path, options),
    DELETE: async (path: string, options?: Options) => write('DELETE', path, options),
  },
}))

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}))

let mounted: VueWrapper | null = null
let router: Router
let pinia: Pinia

beforeEach(() => {
  localStorage.clear()
  calls.length = 0
  rows = [
    command('hello', { useCount: 12, aliases: ['hi'], templateResponse: 'Hello there' }),
    command('socials', { useCount: 40, templateResponse: 'Find me on the web', isEnabled: false }),
    command('pipe', { tier: 'pipeline', pipelineId: null, templateResponse: null, useCount: 3 }),
  ]
  refusePut = null
  refusePost = null
  answers = freshAnswers()
  pinia = createPinia()
  setActivePinia(pinia)
  i18n.global.mergeLocaleMessage('en', en)
})

afterEach(() => {
  mounted?.unmount()
  mounted = null
  document.body.innerHTML = ''
})

const settle = async (): Promise<void> => {
  await flushPromises()
  await new Promise((done) => setTimeout(done, 0))
  await flushPromises()
}

async function open(url: string = '/app/commands'): Promise<void> {
  const blank = { template: '<div />' }
  router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/app/commands', name: 'dashboard-commands', component: blank }],
  })
  await router.push(url)
  mounted = mount(CommandsView, {
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

const rowEls = (): HTMLElement[] => [
  ...document.body.querySelectorAll<HTMLElement>('[data-command-row]'),
]
const rowNames = (): (string | undefined)[] => rowEls().map((row) => row.dataset.commandName)
const editor = (): HTMLElement | null =>
  document.body.querySelector<HTMLElement>('[data-command-editor]')
const buttonByText = (label: string, root: ParentNode = document.body): HTMLButtonElement =>
  [...root.querySelectorAll<HTMLButtonElement>('button')].find(
    (b) => b.textContent?.trim() === label || b.getAttribute('aria-label') === label,
  ) as HTMLButtonElement
const field = (label: string): HTMLInputElement =>
  document.body.querySelector<HTMLInputElement>(`[aria-label="${label}"]`) as HTMLInputElement
const reply = (): HTMLTextAreaElement => editor()!.querySelector('textarea') as HTMLTextAreaElement
const unsaved = (): string => editor()!.querySelector('[data-unsaved]')?.textContent?.trim() ?? ''
const switchOf = (title: string): HTMLButtonElement =>
  [...document.body.querySelectorAll<HTMLElement>('[data-slot="setting-row"]')]
    .find((row) => row.textContent?.includes(title))
    ?.querySelector('button[role="switch"]') as HTMLButtonElement
const writes = (method: string): Call[] => calls.filter((call) => call.method === method)

async function type(el: HTMLInputElement | HTMLTextAreaElement, value: string): Promise<void> {
  el.value = value
  el.dispatchEvent(new Event('input', { bubbles: true }))
  await settle()
}

async function pick(name: string): Promise<void> {
  rowEls()
    .find((row) => row.dataset.commandName === name)!
    .click()
  await settle()
}

describe('CommandsView list', () => {
  it('lists every command with its type, its uses and an Off mark on a disabled one, never the reply text', async () => {
    rows[2] = command('dice', {
      templateResponse: null,
      templateResponses: ['One', 'Two'],
      useCount: 3,
    })
    await open()

    expect(rowNames().sort()).toEqual(['dice', 'hello', 'socials'])
    const hello: string =
      rowEls().find((row) => row.dataset.commandName === 'hello')!.textContent ?? ''
    expect(hello).toContain('!hello')
    expect(hello).toContain('Text reply · 12 uses')
    expect(hello).not.toContain('Hello there')
    const dice: string =
      rowEls().find((row) => row.dataset.commandName === 'dice')!.textContent ?? ''
    expect(dice).toContain('Random reply · 3 uses')
    expect(dice).not.toContain('One')
    const socials: string =
      rowEls().find((row) => row.dataset.commandName === 'socials')!.textContent ?? ''
    expect(socials).toContain('Off')
    expect(hello).not.toContain('Off')
  })

  it('filters by name and by alias, and says so when nothing matches', async () => {
    await open()
    const search: HTMLInputElement = document.body.querySelector<HTMLInputElement>(
      '[data-slot="search-field"] input',
    )!

    await type(search, 'soc')
    expect(rowNames()).toEqual(['socials'])
    await type(search, 'hi')
    expect(rowNames()).toEqual(['hello'])
    await type(search, 'zzz')
    expect(rowNames()).toEqual([])
    expect(document.body.textContent).toContain('No command matches')
  })

  it('orders by most used first and by name when A to Z is chosen', async () => {
    await open()
    expect(rowNames()).toEqual(['socials', 'hello', 'pipe'])

    buttonByText('A to Z').click()
    await settle()

    expect(rowNames()).toEqual(['hello', 'pipe', 'socials'])
  })

  it('keeps the A to Z order after the page is opened again', async () => {
    await open()
    buttonByText('A to Z').click()
    await settle()
    mounted!.unmount()
    mounted = null
    document.body.innerHTML = ''

    await open()

    expect(rowNames()).toEqual(['hello', 'pipe', 'socials'])
  })

  it('shows the empty state with a way to make the first command', async () => {
    rows = []
    await open()

    expect(document.body.textContent).toContain('No commands yet')
    expect(document.body.querySelectorAll('[data-slot="panel-empty"] button').length).toBe(1)
  })

  it('shows an error with Retry, and Retry loads the list again', async () => {
    answers['/commands'] = (): Answer => refused('boom', 500)
    await open()
    expect(document.body.querySelector('[data-slot="panel-error"]')).not.toBeNull()

    answers['/commands'] = freshAnswers()['/commands']!
    buttonByText('Try again').click()
    await settle()

    expect(rowNames().length).toBe(3)
  })
})

describe('CommandsView editor', () => {
  it('opens the command named in the address and fills the form from its row', async () => {
    rows[0] = command('hello', {
      templateResponse: 'Hi chat',
      cooldownSeconds: 7,
      userCooldownSeconds: 30,
      speakWithTts: true,
    })
    await open('/app/commands?command=hello')

    expect(editor()!.textContent).toContain('!hello')
    expect(reply().value).toBe('Hi chat')
    expect(field('Chat cooldown (seconds)').value).toBe('7')
    expect(field('Per-person cooldown (seconds)').value).toBe('30')
    expect(switchOf('Read it aloud').getAttribute('aria-checked')).toBe('true')
  })

  it('keeps the picked command in the address so a refresh keeps it', async () => {
    await open()
    expect(editor()).toBeNull()

    await pick('socials')

    expect(router.currentRoute.value.query.command).toBe('socials')
    expect(reply().value).toBe('Find me on the web')
  })

  it('sends only the changed fields on Save and clears the unsaved count', async () => {
    await open('/app/commands?command=hello')
    expect(unsaved()).toBe('')

    await type(reply(), 'Hello everyone')
    await type(field('Chat cooldown (seconds)'), '20')
    expect(unsaved()).toBe('2 unsaved changes')

    buttonByText('Save changes').click()
    await settle()

    expect(writes('PUT')).toHaveLength(1)
    expect(writes('PUT')[0]!.name).toBe('hello')
    expect(writes('PUT')[0]!.body).toEqual({
      templateResponse: 'Hello everyone',
      cooldownSeconds: 20,
    })
    expect(unsaved()).toBe('')
    expect(buttonByText('Save changes').disabled).toBe(true)
  })

  it('Discard puts the saved values back and sends nothing', async () => {
    await open('/app/commands?command=hello')
    await type(reply(), 'Something else')
    expect(unsaved()).toBe('1 unsaved change')

    buttonByText('Discard').click()
    await settle()

    expect(reply().value).toBe('Hello there')
    expect(unsaved()).toBe('')
    expect(calls).toHaveLength(0)
  })

  it('keeps the form dirty and shows the bot message when a save is refused', async () => {
    refusePut = 'Unknown helper {zzz}'
    await open('/app/commands?command=hello')
    await type(reply(), 'Hi {zzz}')

    buttonByText('Save changes').click()
    await settle()

    expect(editor()!.querySelector('[role="alert"]')?.textContent).toContain('Unknown helper {zzz}')
    expect(unsaved()).toBe('1 unsaved change')
    expect(reply().value).toBe('Hi {zzz}')
  })

  it('switches a command off with a PUT of isEnabled only, and puts it back when refused', async () => {
    await open('/app/commands?command=hello')

    switchOf('Enabled').click()
    await settle()
    expect(writes('PUT')[0]!.body).toEqual({ isEnabled: false })
    expect(rowEls().find((row) => row.dataset.commandName === 'hello')!.textContent).toContain(
      'Off',
    )

    refusePut = 'No right'
    switchOf('Enabled').click()
    await settle()
    expect(rowEls().find((row) => row.dataset.commandName === 'hello')!.textContent).toContain(
      'Off',
    )
  })

  it('sends speakWithTts when Read it aloud is switched on', async () => {
    await open('/app/commands?command=hello')

    switchOf('Read it aloud').click()
    await settle()
    expect(unsaved()).toBe('1 unsaved change')
    buttonByText('Save changes').click()
    await settle()

    expect(writes('PUT')[0]!.body).toEqual({ speakWithTts: true })
  })

  it('turns on random replies, takes a list of replies and sends them as templateResponses', async () => {
    await open('/app/commands?command=hello')

    switchOf('Pick a random reply').click()
    await settle()
    const inputs = (): HTMLTextAreaElement[] => [
      ...editor()!.querySelectorAll<HTMLTextAreaElement>('[data-random-reply] textarea'),
    ]
    expect(inputs()).toHaveLength(1)
    await type(inputs()[0]!, 'One')
    buttonByText('Add a reply').click()
    await settle()
    await type(inputs()[1]!, 'Two')

    buttonByText('Save changes').click()
    await settle()

    expect(writes('PUT')[0]!.body).toEqual({ templateResponses: ['One', 'Two'] })
  })

  it('removes a command only after the confirm, with a DELETE for that name', async () => {
    await open('/app/commands?command=hello')

    buttonByText('Delete').click()
    await settle()
    expect(writes('DELETE')).toHaveLength(0)

    document.body.querySelector<HTMLButtonElement>('[data-confirm-action]')!.click()
    await settle()

    expect(writes('DELETE')).toHaveLength(1)
    expect(writes('DELETE')[0]!.name).toBe('hello')
    expect(rowNames()).not.toContain('hello')
    expect(editor()).toBeNull()
    expect(router.currentRoute.value.query.command).toBeUndefined()
  })

  it('tells the streamer a pipeline command with no pipeline answers nothing', async () => {
    await open('/app/commands?command=pipe')

    expect(editor()!.textContent).toContain('answers nothing')
    expect(editor()!.querySelector('textarea')).toBeNull()
  })

  it('has no Followers rung in the role list', () => {
    expect(permissionLevels).toEqual([
      'Everyone',
      'Subscriber',
      'Vip',
      'Artist',
      'Moderator',
      'LeadModerator',
      'Editor',
      'Broadcaster',
    ])
  })
})

describe('CommandsView new command', () => {
  async function openDialog(): Promise<void> {
    buttonByText('New command').click()
    await settle()
  }

  it('creates a template command with a POST of the cleaned name and the reply, then opens it', async () => {
    await open()
    await openDialog()

    await type(document.body.querySelector<HTMLInputElement>('[data-new-name]')!, '  !Welcome ')
    await type(
      document.body.querySelector<HTMLTextAreaElement>('[role="dialog"] textarea')!,
      'Welcome in',
    )
    document.body.querySelector<HTMLButtonElement>('[data-form-action]')!.click()
    await settle()

    expect(writes('POST')).toHaveLength(1)
    expect(writes('POST')[0]!.body).toMatchObject({
      name: 'welcome',
      tier: 'template',
      templateResponse: 'Welcome in',
    })
    expect(rowNames()).toContain('welcome')
    expect(router.currentRoute.value.query.command).toBe('welcome')
    expect(document.body.querySelector('[role="dialog"]')).toBeNull()
  })

  it('keeps the dialog open and shows the bot message when the name is taken', async () => {
    refusePost = 'A command named hello already exists'
    await open()
    await openDialog()

    await type(document.body.querySelector<HTMLInputElement>('[data-new-name]')!, 'hello')
    await type(document.body.querySelector<HTMLTextAreaElement>('[role="dialog"] textarea')!, 'x')
    document.body.querySelector<HTMLButtonElement>('[data-form-action]')!.click()
    await settle()

    expect(document.body.querySelector('[data-form-error]')?.textContent).toContain(
      'already exists',
    )
    expect(document.body.querySelector('[role="dialog"]')).not.toBeNull()
  })

  it('does not send a command without a name', async () => {
    await open()
    await openDialog()

    document.body.querySelector<HTMLButtonElement>('[data-form-action]')!.click()
    await settle()

    expect(writes('POST')).toHaveLength(0)
  })
})

describe('CommandsView built-in tab', () => {
  it('lists the built-in commands and switches one off with a PATCH of enabled', async () => {
    await open('/app/commands?view=builtin')

    expect(document.body.textContent).toContain('!uptime')
    switchOf('!uptime').click()
    await settle()

    expect(writes('PATCH')).toHaveLength(1)
    expect(writes('PATCH')[0]!.name).toBe('uptime')
    expect(writes('PATCH')[0]!.body).toEqual({ enabled: false })
  })
})
