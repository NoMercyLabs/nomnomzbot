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
import { defineComponent, h, ref } from 'vue'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'

import { chatKey, type ChatLine } from '@/api/chat'
import { Toaster } from '@/components/ui/sonner'
import { TooltipProvider } from '@/components/ui/tooltip'
import { i18n } from '@/i18n'
import { useProfileDialogStore } from '@/stores/profileDialog'

import ChatView from '../ChatView.vue'
import en from '../i18n/en.json'

type Options = { params?: { path?: Record<string, unknown> }; body?: unknown }
type Call = { method: string; path: string; body: unknown; messageId: string }
type Answer = { data?: unknown; response: { status: number; ok: boolean } }

const calls: Call[] = []
let history: ChatLine[] = []
let sendOk: boolean = true

const line = (
  id: string,
  userId: string,
  name: string,
  message: string,
  over: object = {},
): ChatLine =>
  ({
    id,
    channelId: 'c1',
    userId,
    displayName: name,
    username: name.toLowerCase(),
    message,
    fragments: [{ type: 'text', text: message }],
    userType: '',
    isSubscriber: false,
    isVip: false,
    isModerator: false,
    isBroadcaster: false,
    isCheer: false,
    isCommand: false,
    badges: [],
    bitsAmount: 0,
    color: '#ff69b4',
    messageType: 'text',
    replyToMessageId: null,
    replyParentMessageBody: null,
    replyParentUserName: null,
    timestamp: '2026-10-12T18:10:00Z',
    provider: 'twitch',
    ...over,
  }) as unknown as ChatLine

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string): Promise<Answer> => {
      calls.push({ method: 'GET', path, body: undefined, messageId: '' })

      return { data: { data: history }, response: { status: 200, ok: true } }
    },
    POST: async (path: string, options?: Options): Promise<Answer> => {
      calls.push({ method: 'POST', path, body: options?.body, messageId: '' })
      const ok = !path.endsWith('/chat/messages') || sendOk

      return { data: { data: null }, response: { status: ok
        ? 200
        : 500, ok } }
    },
    DELETE: async (path: string, options?: Options): Promise<Answer> => {
      const messageId = String(options?.params?.path?.messageId)

      calls.push({ method: 'DELETE', path, body: undefined, messageId })

      return { data: { data: null }, response: { status: 200, ok: true } }
    },
  },
}))

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({
    channelId: ref('c1'),
    current: ref({ login: 'stoney_eagle', displayName: 'Stoney_Eagle', isLive: true }),
  }),
}))

const ROW_HEIGHT: number = 40
const VIEWPORT_HEIGHT: number = 4000

// jsdom has no layout: the feed is VIEWPORT_HEIGHT tall and every line is ROW_HEIGHT tall.
function stubLayout(): void {
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe(): void {}
      unobserve(): void {}
      disconnect(): void {}
    },
  )

  vi.stubGlobal(
    'IntersectionObserver',
    class {
      observe(): void {}
      unobserve(): void {}
      disconnect(): void {}
    },
  )

  const sizeOf = (element: Element): number =>
    element.hasAttribute('data-virtual-scroll')
      ? VIEWPORT_HEIGHT
      : ROW_HEIGHT

  vi.spyOn(HTMLElement.prototype, 'offsetHeight', 'get').mockImplementation(function (
    this: HTMLElement,
  ) {
    return sizeOf(this)
  })

  vi.spyOn(HTMLElement.prototype, 'clientHeight', 'get').mockImplementation(function (
    this: HTMLElement,
  ) {
    return sizeOf(this)
  })

  vi.spyOn(HTMLElement.prototype, 'offsetWidth', 'get').mockReturnValue(600)

  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (
    this: HTMLElement,
  ) {
    return {
      width: 600,
      height: sizeOf(this),
      top: 0,
      left: 0,
      right: 600,
      bottom: sizeOf(this),
      x: 0,
      y: 0,
    } as DOMRect
  })

  HTMLElement.prototype.scrollTo = function (
    this: HTMLElement,
  ): void {} as typeof HTMLElement.prototype.scrollTo
}

let mounted: VueWrapper | null = null
let pinia: Pinia
let router: Router
let queries: QueryClient

beforeEach(() => {
  calls.length = 0
  localStorage.clear()
  sendOk = true

  history = [
    line('m1', 'u1', 'Alice', 'first words'),
    line('m2', 'u2', 'Bob', 'second words'),
    line('m3', 'u1', 'Alice', '!sr some song', { isCommand: true }),
  ]

  pinia = createPinia()
  setActivePinia(pinia)
  i18n.global.mergeLocaleMessage('en', en)
  stubLayout()
})

afterEach(() => {
  mounted?.unmount()
  mounted = null
  document.body.innerHTML = ''
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

const settle = async (): Promise<void> => {
  await flushPromises()
  await new Promise((done) => setTimeout(done, 0))
  await flushPromises()
}

async function open(): Promise<void> {
  router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/app/chat', name: 'dashboard-chat', component: { template: '<div />' } }],
  })

  await router.push('/app/chat')
  queries = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  // The page runs inside the frame's tooltip provider and toast host, so the test gives it the same.
  const framed = defineComponent(() => () => h(TooltipProvider, () => [h(ChatView), h(Toaster)]))

  mounted = mount(framed, {
    attachTo: document.body,
    global: { plugins: [i18n, pinia, router, [VueQueryPlugin, { queryClient: queries }]] },
  })

  await settle()
}

const rows = (): HTMLElement[] => [...document.body.querySelectorAll<HTMLElement>('[data-line-id]')]
const rowIds = (): string[] => rows().map((row) => row.dataset.lineId ?? '')
const rowOf = (id: string): HTMLElement =>
  rows().find((row) => row.dataset.lineId === id) as HTMLElement
const action = (id: string, name: string): HTMLButtonElement =>
  rowOf(id).querySelector(`[data-line-action="${name}"]`) as HTMLButtonElement

// jsdom has no pointer capture, so the menu is opened the keyboard way (Enter on the trigger).
const openMenu = (trigger: HTMLElement): void => {
  trigger.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }))
}

const input = (): HTMLInputElement =>
  document.body.querySelector('input[data-composer]') as HTMLInputElement
const writes = (): Call[] => calls.filter((call) => call.method !== 'GET')

async function type(value: string): Promise<void> {
  input().value = value
  input().dispatchEvent(new Event('input', { bubbles: true }))
  await settle()
}

async function send(): Promise<void> {
  document.body
    .querySelector('form')!
    .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))

  await settle()
}

describe('ChatView', () => {
  it('renders the history lines in order with their names and messages', async () => {
    await open()
    expect(rowIds()).toEqual(['m1', 'm2', 'm3'])
    expect(rowOf('m2').textContent).toContain('Bob')
    expect(rowOf('m2').textContent).toContain('second words')
  })

  it('shows a line that is pushed into the feed', async () => {
    await open()

    queries.setQueryData<ChatLine[]>(chatKey('c1'), (feed) => [
      ...(feed ?? []),
      line('m4', 'u3', 'Cara', 'just arrived'),
    ])

    await settle()
    expect(rowIds()).toEqual(['m1', 'm2', 'm3', 'm4'])
    expect(rowOf('m4').textContent).toContain('just arrived')
  })

  it.each([
    [{ removed: 'timeout', timeoutSeconds: 600 }, 'Timed out · 10 min'],
    [{ removed: 'timeout', timeoutSeconds: 45 }, 'Timed out · 45 sec'],
    [{ removed: 'timeout', timeoutSeconds: 1_209_600 }, 'Timed out · 14 days'],
    [{ removed: 'banned' }, 'Banned'],
  ] as const)('a removed line says what happened: %o', async (mark, said) => {
    await open()

    queries.setQueryData<ChatLine[]>(chatKey('c1'), (feed) =>
      feed?.map((one) =>
        one.id === 'm1'
          ? { ...one, ...mark }
          : one),
    )

    await settle()
    expect(rowOf('m1').querySelector('[data-removal]')?.textContent?.trim()).toBe(said)
    expect(rowOf('m2').querySelector('[data-removal]')).toBeNull()
  })

  it('strikes a deleted line, and Delete calls DELETE with that line id', async () => {
    await open()

    queries.setQueryData<ChatLine[]>(chatKey('c1'), (feed) =>
      feed?.map((one) =>
        one.id === 'm1'
          ? { ...one, removed: 'deleted' }
          : one),
    )

    await settle()
    expect(rowOf('m1').dataset.removed).toBe('deleted')
    expect(rowOf('m2').dataset.removed).toBeUndefined()

    action('m2', 'delete').click()
    await settle()
    expect(writes()).toEqual([expect.objectContaining({ method: 'DELETE', messageId: 'm2' })])
  })

  it('shows four tray buttons in order: timeout, delete, ban, profile', async () => {
    await open()
    const names = [...rowOf('m2').querySelectorAll<HTMLElement>('[data-line-action]')].map(
      (button) => button.dataset.lineAction,
    )

    expect(names).toEqual(['timeout', 'delete', 'ban', 'profile'])
  })

  it('keeps every tray button a plain ghost icon with a 20px icon, harm only in the ban confirm', async () => {
    await open()

    for (const name of ['timeout', 'delete', 'ban', 'profile']) {
      const button = action('m2', name)

      expect(button.dataset.variant).toBe('ghost')
      expect(button.dataset.size).toBe('icon')
      expect(button.querySelector('svg')!.classList.contains('size-5')).toBe(true)
    }
  })

  it('opens the profile of the line person from the profile button', async () => {
    await open()
    const store = useProfileDialogStore(pinia)

    action('m2', 'profile').click()
    await settle()
    expect(store.userId).toBe('u2')
  })

  it('opens a length menu on Timeout and sends nothing until a length is picked', async () => {
    await open()
    openMenu(action('m2', 'timeout'))
    await settle()
    const items = [...document.body.querySelectorAll<HTMLElement>('[data-timeout-length]')].map(
      (item) => item.dataset.timeoutLength ?? '',
    )

    expect(items).toEqual(['60', '600', '3600', '86400'])
    expect(writes()).toEqual([])
  })

  it('times out the person for the picked length', async () => {
    await open()
    openMenu(action('m2', 'timeout'))
    await settle()
    document.body.querySelector<HTMLElement>('[data-timeout-length="3600"]')!.click()
    await settle()
    expect(writes()).toHaveLength(1)
    expect(writes()[0]!.path).toBe('/api/v{version}/channels/{channelId}/moderation/actions')

    expect(writes()[0]!.body).toEqual({
      action: 'timeout',
      targetUserId: 'u2',
      durationSeconds: 3600,
      reason: null,
    })
  })

  it('has no length filter in the panel header', async () => {
    await open()
    expect(document.body.querySelector('[data-slot="filter-bar"]')).toBeNull()
  })

  it('shows the character counter only past 400 characters', async () => {
    await open()
    await type('x'.repeat(400))
    expect(document.body.querySelector('[data-composer-count]')).toBeNull()
    await type('x'.repeat(401))

    expect(document.body.querySelector('[data-composer-count]')?.textContent?.trim()).toBe(
      '401/500',
    )
  })

  it('bans only after the confirm, in this channel', async () => {
    await open()
    action('m2', 'ban').click()
    await settle()
    expect(writes()).toEqual([])
    const confirm = document.body.querySelector('[data-confirm-action]') as HTMLButtonElement

    confirm.click()
    await settle()
    expect(writes()).toHaveLength(1)
    expect(writes()[0]!.path).toBe('/api/v{version}/channels/{channelId}/moderation/actions/ban')

    expect(writes()[0]!.body).toEqual({
      targetTwitchUserId: 'u2',
      scope: 'this_channel',
      reason: null,
    })
  })

  it('sends the typed message as you, and clears the box', async () => {
    await open()
    await type('hello chat')
    await send()
    expect(writes()).toHaveLength(1)
    expect(writes()[0]!.path).toBe('/api/v{version}/channels/{channelId}/chat/messages')

    expect(writes()[0]!.body).toEqual({
      message: 'hello chat',
      senderIdentity: 'you',
      replyToMessageId: null,
    })

    expect(input().value).toBe('')
  })

  it('blocks a message over 500 characters and says why', async () => {
    await open()
    await type('x'.repeat(501))
    await send()
    expect(writes()).toEqual([])

    expect(document.body.textContent).toContain(
      en.dashboard.chat.composer.tooLong.replace('{max}', '500'),
    )

    expect(document.body.querySelector<HTMLButtonElement>('button[type="submit"]')!.disabled).toBe(
      true,
    )
  })

  it('removes command lines while Hide commands is on', async () => {
    await open()
    expect(rowIds()).toContain('m3')
    document.body.querySelector<HTMLElement>('[role="switch"]')!.click()
    await settle()
    expect(rowIds()).toEqual(['m1', 'm2'])
  })

  it('remembers Hide commands after the page is closed and opened again', async () => {
    await open()
    document.body.querySelector<HTMLElement>('[role="switch"]')!.click()
    await settle()
    mounted?.unmount()
    mounted = null
    await open()
    expect(rowIds()).toEqual(['m1', 'm2'])
    expect(localStorage.getItem('nnz.chat.hideCommands')).toBe('true')
  })

  it('shows the error of a failed send and keeps the text', async () => {
    sendOk = false
    await open()
    await type('will not go')
    await send()
    expect(writes()).toHaveLength(1)

    expect(document.body.querySelector('[role="alert"]')?.textContent).toContain(
      en.dashboard.chat.composer.failed,
    )

    expect(input().value).toBe('will not go')
  })

  it('opens the profile of the person on the line', async () => {
    await open()
    const store = useProfileDialogStore(pinia)

    rowOf('m2').querySelector<HTMLElement>('[data-slot="user-name"]')!.click()
    await settle()
    expect(store.userId).toBe('u2')
  })
})
