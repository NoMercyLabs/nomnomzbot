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
import { afterEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, ref } from 'vue'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'

import { i18n } from '@/i18n'
import DashboardLayout from '@/layouts/DashboardLayout.vue'
import ProfileMenu from '@/layouts/dashboard/ProfileMenu.vue'

const user = ref<{ displayName: string; profileImageUrl: string | null; isAdmin: boolean }>({
  displayName: 'Streamer',
  profileImageUrl: null,
  isAdmin: false,
})

vi.mock('@/api/auth', () =>
  ({
    useCurrentUser: () => ({ data: user }),
    signOut: vi.fn<() => Promise<void>>(),
    // Signed out for the live hubs: these tests cover the frame, not live updates.
    useSignedIn: () => ref(false),
    getAccessToken: () => '',
  }))

vi.mock('@/composables/useCurrentChannel', () =>
  ({
    useCurrentChannel: () => ({
      channels: ref([]),
      current: ref(null),
      channelId: ref(null),
      isPending: ref(false),
      choose: vi.fn<() => Promise<void>>(),
    }),
  }))

// A window of the given width: matchMedia answers min-width queries against it.
function setWidth(width: number): void {
  window.matchMedia = ((query: string): MediaQueryList => {
    const min = query.match(/min-width:\s*(\d+)px/)

    return {
      matches: min
        ? width >= Number(min[1])
        : false,
      media: query,
      addEventListener: () => {},
      removeEventListener: () => {},
      addListener: () => {},
      removeListener: () => {},
      dispatchEvent: () => false,
      onchange: null,
    } as MediaQueryList
  }) as typeof window.matchMedia
}

const stub = (slot: string) => defineComponent({ render: () => h('div', { 'data-stub': slot }) })

function mountLayout(width: number): VueWrapper {
  setWidth(width)

  return mount(DashboardLayout, {
    global: {
      plugins: [i18n],
      stubs: {
        DashboardSidebar: stub('sidebar'),
        NavRail: stub('rail'),
        BottomBar: stub('bottom'),
        ChannelSwitch: stub('switch'),
        ShieldToggle: stub('shield'),
        ErrorInbox: stub('inbox'),
        ProfileMenu: stub('profile'),
        RouterView: stub('view'),
      },
    },
  })
}

function frames(wrapper: VueWrapper): string[] {
  return wrapper.findAll('[data-stub]').map((node) => node.attributes('data-stub') as string)
}

afterEach(() => {
  document.body.innerHTML = ''
  user.value = { displayName: 'Streamer', profileImageUrl: null, isAdmin: false }
})

describe('the dashboard frame by window width', () => {
  it('shows the sidebar from 840 px and no rail or bottom bar', () => {
    const shown = frames(mountLayout(1000))

    expect(shown).toContain('sidebar')
    expect(shown).not.toContain('rail')
    expect(shown).not.toContain('bottom')
  })

  it('puts no bar above the page on a wide screen, so the page header is the only header', () => {
    const wrapper = mountLayout(1000)

    expect(wrapper.find('[data-slot="frame-header"]').exists()).toBe(false)
    expect(frames(wrapper)).not.toContain('shield')
  })

  it('shows the icon rail between 600 and 839 px', () => {
    const shown = frames(mountLayout(700))

    expect(shown).toContain('rail')
    expect(shown).not.toContain('sidebar')
    expect(shown).not.toContain('bottom')
  })

  it('shows the bottom bar under 600 px, with the channel switch in the header', () => {
    const shown = frames(mountLayout(400))

    expect(shown).toContain('bottom')
    expect(shown).toContain('switch')
    expect(shown).not.toContain('sidebar')
    expect(shown).not.toContain('rail')
  })
})

describe('the profile menu', () => {
  const page = defineComponent({ render: () => h('div') })

  async function openMenu(isAdmin: boolean): Promise<string> {
    user.value = { displayName: 'Streamer', profileImageUrl: null, isAdmin }
    setWidth(1000)
    const router: Router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { name: 'dashboard-admin-channels', path: '/admin', component: page },
        { name: 'dashboard-settings', path: '/settings', component: page },
      ],
    })
    const wrapper: VueWrapper = mount(ProfileMenu, {
      attachTo: document.body,
      global: { plugins: [i18n, router] },
    })

    await wrapper.find('[data-slot="profile-menu"]').trigger('click')
    await flushPromises()

    return document.body.textContent ?? ''
  }

  it('offers Platform admin to an admin', async () => {
    expect(await openMenu(true)).toContain('Platform admin')
  })

  it('does not offer Platform admin to anyone else', async () => {
    const text = await openMenu(false)

    expect(text).toContain('Sign out')
    expect(text).not.toContain('Platform admin')
  })
})
