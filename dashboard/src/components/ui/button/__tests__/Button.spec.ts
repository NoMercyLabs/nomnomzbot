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

import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { Fragment, h } from 'vue'
import { createMemoryHistory, createRouter, RouterLink } from 'vue-router'
import Button from '../Button.vue'
import { buttonVariants } from '..'

const tokens = readFileSync(resolve(__dirname, '../../../../styles/tokens.css'), 'utf8')
const frame = readFileSync(resolve(__dirname, '../../../../styles/frame.css'), 'utf8')

// The frame.css rule .group/button[data-size='sm'] sets height from --btn-h-sm and beats the h-9 class,
// so the size is decided by the variable, not by the class names (live: 32px at 390 with correct classes).
describe('--btn-h-sm token', () => {
  it('is 36px below md and 32px from md up', () => {
    const base = /:root \{[\s\S]*?--btn-h-sm: (\d+)px;/.exec(tokens)
    const desktop = /@media \(min-width: 768px\) \{\s*:root \{\s*--btn-h-sm: (\d+)px;/.exec(tokens)

    expect(base?.[1]).toBe('36')
    expect(desktop?.[1]).toBe('32')
  })

  it('drives both the sm height and the icon-sm square in frame.css', () => {
    expect(frame).toMatch(/\[data-size='sm'\] \{\s*height: var\(--btn-h-sm\)/)
    expect(frame).toMatch(/\[data-size='icon-sm'\] \{\s*width: var\(--btn-h-sm\);\s*height: var\(--btn-h-sm\)/)
  })
})

// The phone height is the base class and the desktop height is the md: override.
// A max-md: override loses to the base height in the compiled CSS (live: 32px at 390).
describe('buttonVariants touch height', () => {
  it('keeps size sm at 36px on a phone and 32px from md up', () => {
    const classes = buttonVariants({ size: 'sm' }).split(' ')

    expect(classes).toContain('h-9')
    expect(classes).toContain('md:h-8')
    expect(classes).not.toContain('h-8')
  })

  it('keeps size icon-sm at 36px on a phone and 32px from md up', () => {
    const classes = buttonVariants({ size: 'icon-sm' }).split(' ')

    expect(classes).toContain('size-9')
    expect(classes).toContain('md:size-8')
    expect(classes).not.toContain('size-8')
  })
})

// Slot fragments and translated strings must expose the icon's actual leading/trailing position.
describe('button content geometry', () => {
  it('wraps a plain label after a leading icon without adding a content container', () => {
    const wrapper = mount(Button, { slots: { default: () => [h('svg'), 'Save changes'] } })
    expect(wrapper.element.firstElementChild?.tagName).toBe('svg')
    expect(wrapper.element.lastElementChild?.tagName).toBe('SPAN')
    expect(wrapper.text()).toBe('Save changes')
  })

  it('wraps a label before a trailing icon inside a fragment', () => {
    const wrapper = mount(Button, { slots: { default: () => h(Fragment, ['Next', h('svg')]) } })
    expect(wrapper.element.firstElementChild?.tagName).toBe('SPAN')
    expect(wrapper.element.lastElementChild?.tagName).toBe('svg')
  })

  it('keeps an icon-only control free of label wrappers', () => {
    const wrapper = mount(Button, { props: { size: 'icon', 'aria-label': 'Search' }, slots: { default: () => h('svg') } })
    expect(wrapper.element.children).toHaveLength(1)
    expect(wrapper.attributes('aria-label')).toBe('Search')
  })
})

// Reka clones the direct child to forward the button's attributes and listeners.
describe('button as-child forwarding', () => {
  it('keeps attributes and click handling on the actual anchor', async () => {
    const click = vi.fn()
    const wrapper = mount(Button, {
      props: { asChild: true, variant: 'outline', size: 'sm', onClick: click },
      attrs: { 'data-action': 'open', class: 'min-w-0' },
      slots: { default: () => h('a', { href: '/people' }, 'Open') },
    })
    expect(wrapper.element.tagName).toBe('A')
    expect(wrapper.attributes('href')).toBe('/people')
    expect(wrapper.attributes('data-action')).toBe('open')
    expect(wrapper.attributes('data-variant')).toBe('outline')
    expect(wrapper.attributes('data-size')).toBe('sm')
    expect(wrapper.classes()).toContain('group/button')
    expect(wrapper.classes()).toContain('min-w-0')
    await wrapper.trigger('click')
    expect(click).toHaveBeenCalledOnce()
  })

  it('forwards styling and identity through a RouterLink and still navigates', async () => {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/:pathMatch(.*)*', component: { render: () => null } }],
    })
    await router.push('/')
    const wrapper = mount(Button, {
      props: { asChild: true, variant: 'outline' },
      attrs: { 'data-open-page': '', class: 'min-w-0' },
      slots: { default: () => h(RouterLink, { to: '/people' }, () => 'Open people') },
      global: { plugins: [router] },
    })
    expect(wrapper.element.tagName).toBe('A')
    expect(wrapper.attributes('href')).toBe('/people')
    expect(wrapper.attributes('data-open-page')).toBe('')
    expect(wrapper.attributes('data-variant')).toBe('outline')
    expect(wrapper.classes()).toContain('min-w-0')
    await wrapper.trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.path).toBe('/people')
  })
})
