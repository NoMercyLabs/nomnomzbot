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
import { h } from 'vue'

import FormDialog from '@/components/layout/FormDialog.vue'
import { i18n } from '@/i18n'

let mounted: VueWrapper | null = null

afterEach(() => {
  mounted?.unmount()
  mounted = null
  document.body.innerHTML = ''
})

async function dialog(extra: Record<string, unknown> = {}): Promise<VueWrapper> {
  mounted = mount(FormDialog, {
    props: { open: true, title: 'New command', action: 'Create command', ...extra },
    slots: {
      default: () => [
        h('input', { 'data-testid': 'name', name: 'name' }),
        h('input', { 'data-testid': 'response', name: 'response' }),
      ],
    },
    attachTo: document.body,
    global: { plugins: [i18n] },
  })
  await flushPromises()
  return mounted
}

function primary(): HTMLButtonElement {
  return document.body.querySelector('[data-form-action]') as HTMLButtonElement
}

function field(name: string): HTMLInputElement {
  return document.body.querySelector(`[data-testid="${name}"]`) as HTMLInputElement
}

function press(target: Element, key: string): void {
  target.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }))
}

describe('the form dialog', () => {
  it('puts focus on the first field when it opens', async () => {
    await dialog()

    expect(document.activeElement).toBe(field('name'))
  })

  it('submits once when Enter is pressed in a field', async () => {
    const wrapper: VueWrapper = await dialog()

    press(field('name'), 'Enter')
    await flushPromises()

    expect(wrapper.emitted('submit')).toHaveLength(1)
  })

  it('submits once when the primary button is pressed', async () => {
    const wrapper: VueWrapper = await dialog()

    primary().click()
    await flushPromises()

    expect(wrapper.emitted('submit')).toHaveLength(1)
  })

  it('names the action on the primary button and puts Cancel before it', async () => {
    await dialog()

    const buttons: HTMLButtonElement[] = Array.from(
      document.body.querySelectorAll('[data-slot="dialog-footer"] button'),
    )
    expect(buttons.map((b: HTMLButtonElement) => b.textContent?.trim())).toEqual(['Cancel', 'Create command'])
  })

  it('blocks a second submit while pending, from Enter and from the button', async () => {
    const wrapper: VueWrapper = await dialog({ pending: true })

    expect(primary().disabled).toBe(true)
    expect(primary().querySelector('[role="status"]')).not.toBeNull()
    primary().click()
    press(field('name'), 'Enter')
    await flushPromises()

    expect(wrapper.emitted('submit')).toBeUndefined()
  })

  it('blocks closing by Escape and Cancel while pending', async () => {
    const wrapper: VueWrapper = await dialog({ pending: true })

    press(document.body.querySelector('[role="dialog"]') as Element, 'Escape')
    const cancel: HTMLButtonElement = document.body.querySelector('[data-form-cancel]') as HTMLButtonElement
    expect(cancel.disabled).toBe(true)
    cancel.click()
    await flushPromises()

    expect(wrapper.emitted('update:open')).toBeUndefined()
  })

  it('closes on Escape and on Cancel when not pending, without submitting', async () => {
    const wrapper: VueWrapper = await dialog()

    press(document.body.querySelector('[role="dialog"]') as Element, 'Escape')
    await flushPromises()
    expect(wrapper.emitted('update:open')?.at(-1)).toEqual([false])

    ;(document.body.querySelector('[data-form-cancel]') as HTMLButtonElement).click()
    await flushPromises()
    expect(wrapper.emitted('update:open')).toHaveLength(2)
    expect(wrapper.emitted('submit')).toBeUndefined()
  })

  it('shows a server error above the footer and keeps the typed values', async () => {
    const wrapper: VueWrapper = await dialog()
    field('name').value = 'so'
    field('response').value = 'Go follow them'

    await wrapper.setProps({ error: 'That name is already taken.' })
    await flushPromises()

    const alert: Element = document.body.querySelector('[data-form-error]') as Element
    expect(alert.textContent).toContain('That name is already taken.')
    const footer: Element = document.body.querySelector('[data-slot="dialog-footer"]') as Element
    expect(alert.compareDocumentPosition(footer) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(field('name').value).toBe('so')
    expect(field('response').value).toBe('Go follow them')
  })

  it('shows no error area when there is no error', async () => {
    await dialog()

    expect(document.body.querySelector('[data-form-error]')).toBeNull()
  })
})
