// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { VueQueryPlugin } from '@tanstack/vue-query'
import { siTwitch } from 'simple-icons'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import words from '../i18n/en.json'

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: {} }),
  useRouter: () => ({ replace: vi.fn<() => Promise<void>>() }),
}))

const official: string = 'https://nomnomz.bot'
const twitch = {
  key: 'twitch',
  displayName: 'Twitch',
  flows: ['redirect', 'device_code'],
  enabled: true,
}
const code = {
  deviceCode: 'dc',
  userCode: 'ABCD-EFGH',
  verificationUri: 'https://www.twitch.tv/activate',
  interval: 5,
  expiresIn: 600,
}

function stubBot(): string[] {
  const calls: string[] = []

  vi.stubGlobal(
    'fetch',
    vi.fn(async (request: Request) => {
      calls.push(`${request.method} ${request.url}`)
      const path = new URL(request.url).pathname

      if (path.endsWith('/auth/providers'))
        return Response.json({ data: [twitch] })

      if (path.endsWith('/auth/twitch/device'))
        return Response.json({ data: code })

      return Response.json({ data: { status: 'pending' } })
    }),
  )

  return calls
}

async function mountLogin(): Promise<VueWrapper> {
  vi.resetModules()
  const { i18n } = await import('@/i18n')

  i18n.global.mergeLocaleMessage('en', words)
  const { queryClient } = await import('@/api/queryClient')
  const LoginView = (await import('../LoginView.vue')).default
  const wrapper: VueWrapper = mount(LoginView, {
    global: { plugins: [createPinia(), [VueQueryPlugin, { queryClient }], i18n] },
  })

  await flushPromises()

  return wrapper
}

beforeEach(() => {
  localStorage.clear()
})

afterEach(() => {
  vi.unstubAllGlobals()
})

// A browser sign-in returns to the bot's own site, so another bot is signed in to with a code
// (Stoney 2026-10-13, translated from Dutch: "I must be able to use the SaaS with my own local client").
describe('signing in to a bot this page does not belong to', () => {
  it('names that bot and starts a code sign-in on it, not a redirect', async () => {
    localStorage.setItem('nnz.bot', official)
    const calls = stubBot()
    const wrapper = await mountLogin()

    expect(wrapper.text()).toContain('nomnomz.bot')
    expect(wrapper.text()).toContain('From here, you sign in with a code.')
    expect(wrapper.text()).not.toContain('Sign in with a code instead')

    await wrapper
      .findAll('button')
      .find((b) => b.text() === 'Continue with Twitch')!
      .trigger('click')
    await flushPromises()

    expect(calls).toContain(`POST ${official}/api/v1/auth/twitch/device`)
    expect(wrapper.text()).toContain('ABCD-EFGH')
  })
})

// Twitch and Google only return to https or localhost. Stoney 2026-10-13 (translated from Dutch), on his phone at 192.168.2.60:
// "I am being redirected to localhost again".
describe('signing in from a plain http address on the network', () => {
  it('uses a code for the bot this page belongs to, because no platform will return to that address', async () => {
    vi.stubGlobal('isSecureContext', false)
    const calls = stubBot()
    const wrapper = await mountLogin()

    expect(wrapper.text()).toContain('On this address, you sign in with a code.')
    expect(wrapper.text()).not.toContain('has its own sign-in page')

    await wrapper
      .findAll('button')
      .find((b) => b.text() === 'Continue with Twitch')!
      .trigger('click')
    await flushPromises()

    expect(
      calls.some((call) => call.startsWith('POST ') && call.endsWith('/api/v1/auth/twitch/device')),
    ).toBe(true)
    expect(wrapper.text()).toContain('ABCD-EFGH')
  })
})

// Stoney 2026-10-13 (translated from Dutch): "the login button must still get the logo of the platform, neatly. There must be a
// branding with a welcome and a continue. And a condition (terms) must be added."
describe('the sign-in options', () => {
  it('welcome the visitor, give each platform its own logo, and state the terms', async () => {
    stubBot()
    const wrapper = await mountLogin()

    expect(wrapper.find('h1').text()).toMatch(/NomNomzBot$/)
    expect(wrapper.find('h2').text()).toBe('Welcome')
    const button = wrapper.findAll('button').find((b) => b.text() === 'Continue with Twitch')!

    expect(button.find('svg path').attributes('d')).toBe(siTwitch.path)
    expect(button.find('svg').attributes('aria-hidden')).toBe('true')
    expect(wrapper.text()).toContain(
      'By continuing, you agree to the terms of service and the privacy policy.',
    )
  })
})
