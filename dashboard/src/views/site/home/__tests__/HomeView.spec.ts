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
import { afterEach, describe, expect, it } from 'vitest'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'
import { defineComponent } from 'vue'
import { RouterView } from 'vue-router'

import { i18n } from '@/i18n'
import { siteRoutes } from '@/router/site'

const Shell = defineComponent({ components: { RouterView }, template: '<RouterView />' })

async function openRoot(): Promise<{ wrapper: VueWrapper; router: Router }> {
  const router: Router = createRouter({ history: createMemoryHistory(), routes: [siteRoutes] })

  await router.push('/')
  const wrapper: VueWrapper = mount(Shell, { global: { plugins: [router, i18n] } })

  await flushPromises()

  return { wrapper, router }
}

afterEach(() => {
  document.body.innerHTML = ''
})

describe('the site root', () => {
  it('shows the home page instead of redirecting to sign in', async () => {
    const { wrapper, router } = await openRoot()

    expect(router.currentRoute.value.name).toBe('home')
    expect(wrapper.find('h1').text()).toBe('Run your stream. Skip the busywork.')
  })

  it('sends every connect action to the login route, not to the app', async () => {
    const { wrapper, router } = await openRoot()
    const login: string = router.resolve({ name: 'login' }).href

    const connects: ReturnType<VueWrapper['findAll']> = wrapper
      .findAll('a')
      .filter((link) => link.text() === 'Connect your channel')

    expect(connects.length).toBeGreaterThanOrEqual(3)

    for (const link of connects) {
      expect(link.attributes('href')).toBe(login)
    }

    expect(wrapper.findAll('a').some((link) => link.attributes('href') === '/app')).toBe(false)
  })

  // Stoney 2026-10-13, on a phone (translated from Dutch): "it is not really user-friendly on mobile, is it."
  it('puts the links, the language and connect behind one menu button on a small screen', async () => {
    const { wrapper } = await openRoot()

    await wrapper.find('button[aria-label="Open menu"]').trigger('click')
    await flushPromises()

    const panel: HTMLElement | null = document.querySelector('[role="dialog"]')

    expect(panel).not.toBeNull()
    const links: string[] = [...panel!.querySelectorAll('a')].map((link: HTMLAnchorElement) =>
      link.textContent!.trim(),
    )

    expect(links).toEqual([
      'Features',
      'Self-host vs hosted',
      'Integrations',
      'GitHub',
      'Connect your channel',
    ])

    expect(panel!.querySelector('[data-slot="select-trigger"]')).not.toBeNull()
  })

  it('keeps the license restriction next to the hosting choice', async () => {
    const { wrapper } = await openRoot()

    expect(wrapper.text()).toContain(
      'Only NoMercy Labs may operate NomNomzBot as a paid service for other streamers',
    )

    expect(wrapper.text()).toContain('Running your own bot stays free and unrestricted, forever')
  })

  it('switches the demo panel when another tab is chosen', async () => {
    const { wrapper } = await openRoot()
    const tabs: ReturnType<VueWrapper['findAll']> = wrapper
      .findAll('[role="tablist"]')[0]!
      .findAll('[role="tab"]')
    const shown = (): string => wrapper.find('[role="tabpanel"][data-state="active"]').text()

    expect(tabs[0]!.attributes('aria-selected')).toBe('true')
    expect(shown()).toContain('Chatters today')

    const moderation = tabs.find((tab) => tab.text() === 'Queue')!

    await moderation.trigger('mousedown')
    await flushPromises()

    expect(moderation.attributes('aria-selected')).toBe('true')
    expect(tabs[0]!.attributes('aria-selected')).toBe('false')
    expect(shown()).toContain('reported: posting the same link 6 times')
    expect(shown()).not.toContain('Chatters today')
  })
})
