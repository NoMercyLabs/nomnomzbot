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
import { defineComponent, ref } from 'vue'

import ProfileDialog from '@/components/layout/ProfileDialog.vue'
import UserName from '@/components/layout/UserName.vue'
import { i18n } from '@/i18n'
import { useProfileDialogStore } from '@/stores/profileDialog'
import { useSaveErrorsStore } from '@/stores/saveErrors'

type Call = { path: string; params: Record<string, unknown> | undefined; body: unknown }

const calls: Call[] = []
let vipWrite: Promise<unknown> | null = null
let vipWriteFails = false

// Fresh per test: a VIP save changes the stored person, as the server's record would.
let people: Record<string, Record<string, unknown>> = {}

const freshPeople = (): Record<string, Record<string, unknown>> => ({
  u1: {
    id: 'u1',
    internalUserId: 'g1',
    username: 'ada',
    displayName: 'Ada',
    profileImageUrl: null,
    messageCount: 1234,
    watchHours: 12.5,
    commandsUsed: 7,
    trustLevel: 'viewer',
    isBanned: false,
    isVip: false,
    isModerator: false,
    firstSeen: '2026-01-02T10:00:00Z',
    lastSeen: '2026-10-01T10:00:00Z',
    recentActivity: [
      { type: 'chat', content: 'hello from ada', timestamp: '2026-10-01T10:00:00Z' },
      { type: 'chat', content: 'second line from ada', timestamp: '2026-10-01T10:01:00Z' },
    ],
    banHistory: [],
  },
  u2: {
    id: 'u2',
    internalUserId: 'g2',
    username: 'bo',
    displayName: 'Bo',
    profileImageUrl: null,
    messageCount: 5,
    watchHours: 0.5,
    commandsUsed: 0,
    trustLevel: 'moderator',
    isBanned: true,
    isVip: null,
    isModerator: null,
    firstSeen: '2026-02-02T10:00:00Z',
    lastSeen: '2026-09-01T10:00:00Z',
    recentActivity: [],
    banHistory: [
      {
        id: 'b1',
        bannedBy: 'Stoney_Eagle',
        reason: 'spam links',
        bannedAt: '2026-09-01T10:00:00Z',
        unbannedAt: null,
      },
    ],
  },
})

type Options = { params?: Record<string, unknown>; body?: unknown }

function record(path: string, options: Options | undefined): void {
  calls.push({ path, params: options?.params, body: options?.body })
}

function userIdOf(options: Options | undefined): string {
  return String((options?.params?.path as Record<string, unknown> | undefined)?.userId)
}

vi.mock('@/api/client', () =>
  ({
    client: {
      GET: vi.fn<(path: string, options?: Options) => Promise<unknown>>(async (path, options) => {
        record(path, options)

        return { data: { data: { ...people[userIdOf(options)] } } }
      }),
      POST: vi.fn<(path: string, options?: Options) => Promise<unknown>>(async (path, options) => {
        record(path, options)

        if (path.endsWith('/vip')) {
          if (vipWrite) {
            await vipWrite
          }

          if (vipWriteFails)
            return { error: { status: 'error' }, response: { ok: false } }

          people[userIdOf(options)]!.isVip = true
        }

        return { response: { ok: true } }
      }),
      DELETE: vi.fn<(path: string, options?: Options) => Promise<unknown>>(async (path, options) => {
        record(path, options)
        people[userIdOf(options)]!.isVip = false

        return { response: { ok: true } }
      }),
    },
  }))

vi.mock('@/composables/useCurrentChannel', () =>
  ({
    useCurrentChannel: () => ({ channelId: ref('c1') }),
  }))

let mounted: VueWrapper | null = null
let pinia: Pinia

// jsdom has no layout: the chat list needs a viewport and row heights to render any rows.
beforeEach(() => {
  calls.length = 0
  people = freshPeople()
  vipWrite = null
  vipWriteFails = false
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe(): void {}
      unobserve(): void {}
      disconnect(): void {}
    },
  )
  const sizeOf = (element: Element): number =>
    element.hasAttribute('data-virtual-scroll')
      ? 300
      : 40

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
  vi.spyOn(HTMLElement.prototype, 'offsetWidth', 'get').mockReturnValue(300)
  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (
    this: HTMLElement,
  ) {
    return {
      width: 300,
      height: sizeOf(this),
      top: 0,
      left: 0,
      right: 300,
      bottom: sizeOf(this),
      x: 0,
      y: 0,
    } as DOMRect
  })
  pinia = createPinia()
  setActivePinia(pinia)
})

afterEach(() => {
  mounted?.unmount()
  mounted = null
  document.body.innerHTML = ''
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

const Page = defineComponent({
  components: { ProfileDialog, UserName },
  template: `<div><UserName user-id="u1" name="Ada" /><UserName user-id="u2" name="Bo" /><ProfileDialog /></div>`,
})

async function openPage(): Promise<void> {
  mounted = mount(Page, {
    attachTo: document.body,
    global: {
      plugins: [
        i18n,
        pinia,
        [
          VueQueryPlugin,
          { queryClient: new QueryClient({ defaultOptions: { queries: { retry: false } } }) },
        ],
      ],
    },
  })
  await flushPromises()
}

const name = (id: string): HTMLButtonElement =>
  document.body.querySelector(`[data-user="${id}"]`) as HTMLButtonElement
const dialogs = (): NodeListOf<Element> => document.body.querySelectorAll('[role="dialog"]')
const button = (selector: string): HTMLButtonElement =>
  document.body.querySelector(selector) as HTMLButtonElement

const settle = async (): Promise<void> => {
  await flushPromises()
  await new Promise((done) => setTimeout(done, 0))
  await flushPromises()
}

async function openUser(id: string): Promise<void> {
  name(id).focus()
  name(id).click()
  await settle()
}

describe('the profile dialog', () => {
  it('opens one dialog with the data of the clicked name', async () => {
    await openPage()
    expect(dialogs().length).toBe(0)

    await openUser('u1')

    expect(dialogs().length).toBe(1)
    const text = dialogs()[0]!.textContent ?? ''

    expect(text).toContain('Ada')
    expect(text).toContain('@ada')
    const detail = calls.find((c: Call) => c.path.endsWith('/community/{userId}'))

    expect(detail?.params).toEqual({ path: { version: '1', channelId: 'c1', userId: 'u1' } })
  })

  it('swaps the user in the same dialog when another name opens', async () => {
    await openPage()
    await openUser('u1')

    useProfileDialogStore().open('u2', name('u2'))
    await settle()

    expect(dialogs().length).toBe(1)
    const text = dialogs()[0]!.textContent ?? ''

    expect(text).toContain('Bo')
    expect(text).not.toContain('@ada')
  })

  it('shows the chat lines on the Chat tab and the counts on the Activity tab', async () => {
    await openPage()
    await openUser('u1')
    expect(dialogs()[0]!.textContent).not.toContain('hello from ada')

    const tabs: HTMLElement[] = Array.from(document.body.querySelectorAll('[role="tab"]'))

    expect(tabs.map((t: HTMLElement) => t.textContent?.trim())).toEqual([
      'Overview',
      'Chat',
      'Activity',
      'Moderation',
    ])

    tabs[1]!.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, button: 0 }))
    await settle()
    expect(dialogs()[0]!.textContent).toContain('hello from ada')

    tabs[2]!.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, button: 0 }))
    await settle()
    const stats = dialogs()[0]!.textContent ?? ''

    expect(stats).toContain('1,234')
    expect(stats).toContain('12.5')
    expect(stats).not.toContain('hello from ada')
  })

  it('lists the ban history of a banned person on the Moderation tab', async () => {
    await openPage()
    await openUser('u2')

    const tabs: HTMLElement[] = Array.from(document.body.querySelectorAll('[role="tab"]'))

    tabs[3]!.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, button: 0 }))
    await settle()

    expect(dialogs()[0]!.textContent).toContain('spam links')
    expect(dialogs()[0]!.textContent).toContain('Stoney_Eagle')
  })

  it('asks before it bans and calls the ban endpoint only after the confirm', async () => {
    await openPage()
    await openUser('u1')

    button('[data-profile-ban]').click()
    await settle()
    expect(document.body.querySelector('[role="alertdialog"]')).not.toBeNull()
    expect(calls.filter((c: Call) => c.path.endsWith('/ban'))).toEqual([])

    button('[data-confirm-action]').click()
    await settle()

    const bans = calls.filter((c: Call) => c.path.endsWith('/ban'))

    expect(bans.length).toBe(1)
    expect(bans[0]!.params).toEqual({ path: { version: '1', channelId: 'c1', userId: 'u1' } })
    expect(typeof (bans[0]!.body as { reason: string }).reason).toBe('string')
    expect(document.body.querySelector('[role="alertdialog"]')).toBeNull()
  })

  it('does not ban when the confirm is cancelled', async () => {
    await openPage()
    await openUser('u1')

    button('[data-profile-ban]').click()
    await settle()
    const cancel = Array.from(document.body.querySelectorAll('[role="alertdialog"] button')).find(
      (b: Element) => b.textContent?.trim() === 'Cancel',
    ) as HTMLButtonElement

    cancel.click()
    await settle()

    expect(calls.filter((c: Call) => c.path.endsWith('/ban'))).toEqual([])
  })

  it('flips the VIP button at once and keeps it when the save works', async () => {
    await openPage()
    await openUser('u1')
    expect(button('[data-profile-vip]').textContent).toContain('Make VIP')

    let release: () => void = () => {}

    vipWrite = new Promise<void>((done) => {
      release = done
    })
    button('[data-profile-vip]').click()
    await flushPromises()
    expect(button('[data-profile-vip]').textContent).toContain('Remove VIP')

    release()
    await settle()
    expect(button('[data-profile-vip]').textContent).toContain('Remove VIP')
    expect(calls.some((c: Call) => c.path.endsWith('/vip'))).toBe(true)
  })

  it('rolls the VIP button back and tells the error inbox when the save fails', async () => {
    await openPage()
    await openUser('u1')
    vipWriteFails = true

    button('[data-profile-vip]').click()
    await settle()

    expect(button('[data-profile-vip]').textContent).toContain('Make VIP')
    expect(useSaveErrorsStore().items.length).toBe(1)
  })

  it('takes the VIP state from the person Twitch was asked about, not from a VIP list', async () => {
    people.u1!.isVip = true
    await openPage()
    await openUser('u1')

    expect(button('[data-profile-vip]').textContent).toContain('Remove VIP')
    expect(calls.filter((c: Call) => !c.path.endsWith('/community/{userId}'))).toEqual([])
  })

  it('says the VIP state is unknown and offers no VIP change when Twitch could not be asked', async () => {
    await openPage()
    await openUser('u2')

    expect(button('[data-profile-vip]').disabled).toBe(true)
    expect(button('[data-profile-vip]').textContent).toContain('VIP status unknown')
  })

  it('closes on Esc and returns the focus to the name that opened it', async () => {
    await openPage()
    await openUser('u1')
    expect(dialogs().length).toBe(1)

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
    await settle()

    expect(dialogs().length).toBe(0)
    expect(useProfileDialogStore().userId).toBeNull()
    expect(document.activeElement).toBe(name('u1'))
  })
})
