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
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { nextTick } from 'vue'

import VirtualList from '@/components/layout/VirtualList.vue'
import { i18n } from '@/i18n'

interface Row {
  id: number
}

const ROW_HEIGHT = 40
const VIEWPORT_HEIGHT = 400

// jsdom has no layout (TanStack Virtual's own tests mock sizes the same way): the scroll area is
// VIEWPORT_HEIGHT tall and every row is ROW_HEIGHT tall. Scrolling writes scrollTop and fires a scroll event.
beforeEach(() => {
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
  // The scrollable height is the height the list sets on its inner column.
  vi.spyOn(HTMLElement.prototype, 'scrollHeight', 'get').mockImplementation(function (
    this: HTMLElement,
  ) {
    return parseInt(
      (this.querySelector('[role="list"]') as HTMLElement | null)?.style.height ?? '0',
      10,
    )
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
  HTMLElement.prototype.scrollTo = function (
    this: HTMLElement,
    options?: ScrollToOptions | number,
  ): void {
    this.scrollTop = typeof options === 'object'
      ? (options.top ?? 0)
      : 0
    this.dispatchEvent(new Event('scroll'))
  } as typeof HTMLElement.prototype.scrollTo
})

afterEach(() => {
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

function rows(count: number): Row[] {
  return Array.from({ length: count }, (_: unknown, id: number) =>
    ({ id }))
}

function list(items: Row[], extra: Record<string, unknown> = {}): VueWrapper {
  return mount(VirtualList, {
    props: {
      items,
      itemKey: (row: unknown) => (row as Row).id,
      estimateSize: ROW_HEIGHT,
      label: 'Rows',
      ...extra,
    },
    slots: {
      default: '<template #default="{ item, index }">row-{{ item.id }}-at-{{ index }}</template>',
    },
    attachTo: document.body,
    global: { plugins: [i18n] },
  })
}

function scroller(wrapper: VueWrapper): HTMLElement {
  return wrapper.find('[data-virtual-scroll]').element as HTMLElement
}

async function scrollTo(wrapper: VueWrapper, top: number): Promise<void> {
  const element = scroller(wrapper)

  element.scrollTop = top
  element.dispatchEvent(new Event('scroll'))
  await nextTick()
}

const BOTTOM_OF = (count: number): number => count * ROW_HEIGHT - VIEWPORT_HEIGHT

describe('the virtual list window', () => {
  it('keeps only a window of 5,000 rows in the DOM, each with its item and index', async () => {
    const wrapper = list(rows(5000))

    await nextTick()

    const shown: number = wrapper.findAll('[role="listitem"]').length

    expect(shown).toBeGreaterThan(0)
    expect(shown).toBeLessThan(60)
    expect(wrapper.text()).toContain('row-0-at-0')
    expect(wrapper.text()).not.toContain('row-4999-at-4999')
    expect(wrapper.find('[role="list"]').attributes('aria-label')).toBeUndefined()
    expect(wrapper.find('[data-virtual-scroll]').attributes('aria-label')).toBe('Rows')
    wrapper.unmount()
  })

  it('renders the rows that scrolling brings in, keyed by the item and not by position', async () => {
    const wrapper = list(rows(5000))

    await nextTick()
    await scrollTo(wrapper, 2000)

    expect(wrapper.text()).toContain('row-50-at-50')
    expect(wrapper.text()).not.toContain('row-0-at-0')
    expect(wrapper.find('[data-key="50"]').attributes('aria-posinset')).toBe('51')
    expect(wrapper.find('[data-key="50"]').attributes('aria-setsize')).toBe('5000')
    wrapper.unmount()
  })

  it('keeps a row element when an item is added before it', async () => {
    const wrapper = list(rows(10))

    await nextTick()
    const before: Element = wrapper.find('[data-key="3"]').element

    await wrapper.setProps({ items: [{ id: 99 }, ...rows(10)] })
    await nextTick()

    expect(wrapper.find('[data-key="3"]').element).toBe(before)
    expect(wrapper.find('[data-key="3"]').text()).toBe('row-3-at-4')
    wrapper.unmount()
  })
})

describe('the keyboard', () => {
  it('scrolls by a row with the arrows and by a page with the page keys', async () => {
    const wrapper = list(rows(500))

    await nextTick()
    const area = scroller(wrapper)

    expect(area.getAttribute('tabindex')).toBe('0')

    await wrapper.find('[data-virtual-scroll]').trigger('keydown', { key: 'ArrowDown' })
    expect(area.scrollTop).toBe(ROW_HEIGHT)
    await wrapper.find('[data-virtual-scroll]').trigger('keydown', { key: 'PageDown' })
    expect(area.scrollTop).toBe(ROW_HEIGHT + VIEWPORT_HEIGHT)
    await wrapper.find('[data-virtual-scroll]').trigger('keydown', { key: 'ArrowUp' })
    expect(area.scrollTop).toBe(VIEWPORT_HEIGHT)
    await wrapper.find('[data-virtual-scroll]').trigger('keydown', { key: 'End' })
    expect(area.scrollTop).toBe(BOTTOM_OF(500))
    wrapper.unmount()
  })
})

describe('the stick-to-bottom mode', () => {
  it('starts at the bottom and stays there when a row arrives', async () => {
    const wrapper: VueWrapper = list(rows(50), { stick: true })

    await nextTick()
    await nextTick()
    expect(scroller(wrapper).scrollTop).toBe(BOTTOM_OF(50))

    await wrapper.setProps({ items: rows(51) })
    await nextTick()
    await nextTick()

    expect(scroller(wrapper).scrollTop).toBe(BOTTOM_OF(51))
    expect(wrapper.text()).toContain('row-50-at-50')
    expect(wrapper.find('[data-new-below]').exists()).toBe(false)
    wrapper.unmount()
  })

  it('keeps the place of a reader who scrolled up, counts the new rows and jumps back on request', async () => {
    const wrapper: VueWrapper = list(rows(50), { stick: true })

    await nextTick()
    await nextTick()
    await scrollTo(wrapper, 400)

    await wrapper.setProps({ items: rows(52) })
    await nextTick()
    await nextTick()

    expect(scroller(wrapper).scrollTop).toBe(400)
    const jump = wrapper.find('[data-new-below]')

    expect(jump.text()).toBe('2 new messages below')

    await wrapper.setProps({ items: rows(53) })
    await nextTick()
    expect(wrapper.find('[data-new-below]').text()).toBe('3 new messages below')

    await wrapper.find('[data-new-below]').trigger('click')
    await nextTick()
    await nextTick()

    expect(scroller(wrapper).scrollTop).toBe(BOTTOM_OF(53))
    expect(wrapper.find('[data-new-below]').exists()).toBe(false)
    wrapper.unmount()
  })

  it('clears the count when the reader scrolls to the bottom by hand', async () => {
    const wrapper: VueWrapper = list(rows(50), { stick: true })

    await nextTick()
    await nextTick()
    await scrollTo(wrapper, 400)
    await wrapper.setProps({ items: rows(51) })
    await nextTick()
    expect(wrapper.find('[data-new-below]').exists()).toBe(true)

    await scrollTo(wrapper, BOTTOM_OF(51))

    expect(wrapper.find('[data-new-below]').exists()).toBe(false)
    wrapper.unmount()
  })

  it('does not count or move when the list is not in stick mode', async () => {
    const wrapper = list(rows(50))

    await nextTick()
    await scrollTo(wrapper, 400)

    await wrapper.setProps({ items: rows(52) })
    await nextTick()

    expect(scroller(wrapper).scrollTop).toBe(400)
    expect(wrapper.find('[data-new-below]').exists()).toBe(false)
    wrapper.unmount()
  })
})
