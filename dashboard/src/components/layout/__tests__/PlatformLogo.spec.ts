// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { mount, type VueWrapper } from '@vue/test-utils'
import { siKick, siTwitch, siYoutube } from 'simple-icons'
import { describe, expect, it } from 'vitest'

import PlatformLogo from '@/components/layout/PlatformLogo.vue'

function fillOf(platform: string): string | undefined {
  const wrapper: VueWrapper = mount(PlatformLogo, { props: { platform } })

  return wrapper.find('svg').attributes('fill')
}

// Stoney 2026-10-13 (translated from Dutch): "please use the platform colours".
describe('the platform logo', () => {
  it('paints each platform in its own brand colour', () => {
    expect(fillOf('twitch')).toBe(`#${siTwitch.hex}`)
    expect(fillOf('kick')).toBe(`#${siKick.hex}`)
    expect(fillOf('youtube')).toBe(`#${siYoutube.hex}`)
  })

  it('draws a black-and-white brand in the text colour, so it shows on dark and light', () => {
    expect(fillOf('x')).toBe('currentColor')
    expect(fillOf('twitter')).toBe('currentColor')
  })

  it('takes the text colour on a filled button', () => {
    const wrapper: VueWrapper = mount(PlatformLogo, { props: { platform: 'twitch', mono: true } })

    expect(wrapper.find('svg').attributes('fill')).toBe('currentColor')
  })

  it('draws nothing for a platform it has no logo for', () => {
    const wrapper: VueWrapper = mount(PlatformLogo, { props: { platform: 'myspace' } })

    expect(wrapper.find('svg').exists()).toBe(false)
  })
})
