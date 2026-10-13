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
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { defineComponent, h, ref, type Ref } from 'vue'

import HelperPanel from '@/components/layout/HelperPanel.vue'
import TemplateField from '@/components/layout/TemplateField.vue'
import { i18n } from '@/i18n'

let mounted: VueWrapper | null = null
let text: Ref<string>

// The fetch stand-in answers like the bot: the helper list for a context, wrapped in { data }.
// It is set before the shared client is created, because the client keeps the fetch it finds then.
const bot: { seen: string[] } = vi.hoisted(() => {
  const state: { seen: string[] } = { seen: [] }
  const data = [
    { key: 'user', descriptionKey: 'template.helper.user', sample: 'DukaSoft' },
    { key: 'user.name', descriptionKey: 'template.helper.user_name', sample: 'DukaSoft' },
    { key: 'channel', descriptionKey: 'template.helper.channel', sample: 'stoney_eagle' },
    { key: 'args.<n>', descriptionKey: 'template.helper.args', sample: 'hello' },
  ]

  globalThis.fetch = (async (request: Request) => {
    state.seen.push(new URL(request.url).pathname + new URL(request.url).search)

    return new Response(JSON.stringify({ data }), { status: 200 })
  }) as typeof fetch

  return state
})

beforeEach(() => {
  bot.seen.length = 0
})

afterEach(() => {
  mounted?.unmount()
  mounted = null
  document.body.innerHTML = ''
})

async function host(
  initial: string = '',
  withPanel: boolean = false,
): Promise<HTMLTextAreaElement> {
  text = ref(initial)
  const fieldRef: Ref<InstanceType<typeof TemplateField> | null> = ref(null)
  const Host = defineComponent({
    setup: () => () =>
      h('div', [
        h(TemplateField, {
          ref: fieldRef,
          modelValue: text.value,
          'onUpdate:modelValue': (value: string) => (text.value = value),
          label: 'Reply',
          context: 'command',
        }),
        withPanel
          ? h(HelperPanel, { target: fieldRef.value, context: 'command' })
          : null,
      ]),
  })

  mounted = mount(Host, {
    attachTo: document.body,
    global: {
      plugins: [
        i18n,
        [
          VueQueryPlugin,
          { queryClient: new QueryClient({ defaultOptions: { queries: { retry: false } } }) },
        ],
      ],
    },
  })

  await flushPromises()

  return document.body.querySelector('textarea') as HTMLTextAreaElement
}

async function type(
  area: HTMLTextAreaElement,
  value: string,
  caret: number = value.length,
): Promise<void> {
  area.focus()
  area.value = value
  area.setSelectionRange(caret, caret)
  area.dispatchEvent(new Event('input', { bubbles: true }))
  await flushPromises()
}

async function press(area: HTMLTextAreaElement, key: string): Promise<void> {
  area.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }))
  await flushPromises()
}

function options(): string[] {
  return [...document.body.querySelectorAll('[role="option"]')].map(
    (option: Element) => option.getAttribute('data-helper') ?? '',
  )
}

describe('the template field', () => {
  it('asks the bot for the helpers of its context', async () => {
    await host()

    expect(bot.seen).toEqual(['/api/v1/templates/helpers?context=command'])
  })

  it('opens the helper suggestions when a brace is typed', async () => {
    const area = await host()

    expect(options()).toEqual([])

    await type(area, 'Hi {')

    expect(options()).toEqual(['user', 'user.name', 'channel', 'args.<n>'])
    expect(document.activeElement).toBe(area)
  })

  it('narrows the suggestions as the person keeps typing', async () => {
    const area = await host()

    await type(area, 'Hi {us')

    expect(options()).toEqual(['user', 'user.name'])
  })

  it('inserts the chosen helper in braces and puts the caret after it', async () => {
    const area = await host()

    await type(area, 'Hi {us there', 6)

    await press(area, 'ArrowDown')
    await press(area, 'Enter')

    expect(text.value).toBe('Hi {user.name} there')
    expect(area.value).toBe('Hi {user.name} there')
    expect(area.selectionStart).toBe('Hi {user.name}'.length)
    expect(area.selectionEnd).toBe('Hi {user.name}'.length)
    expect(options()).toEqual([])
    expect(document.activeElement).toBe(area)
  })

  it('inserts with Tab as well, and leaves the text alone for a plain Enter when nothing is open', async () => {
    const area = await host()

    await type(area, '{ch')
    await press(area, 'Tab')
    expect(text.value).toBe('{channel}')

    await type(area, 'done')
    const enter: KeyboardEvent = new KeyboardEvent('keydown', {
      key: 'Enter',
      bubbles: true,
      cancelable: true,
    })

    area.dispatchEvent(enter)
    expect(enter.defaultPrevented).toBe(false)
  })

  it('closes the suggestions on Escape and keeps the text', async () => {
    const area = await host()

    await type(area, 'Hi {us')

    await press(area, 'Escape')

    expect(options()).toEqual([])
    expect(text.value).toBe('Hi {us')
    expect(area.value).toBe('Hi {us')
    expect(document.activeElement).toBe(area)
  })

  it('warns quietly about a placeholder the bot does not know', async () => {
    const area = await host()

    await type(area, 'Hi {user} {nope} {args.2}')

    const warning = document.body.querySelector('[data-template-warning]') as HTMLElement

    expect(warning.textContent).toBe(i18n.global.t('template.field.unknown', { names: '{nope}' }))
    expect(area.getAttribute('aria-invalid')).not.toBe('true')
  })

  it('shows no warning when every placeholder is known', async () => {
    const area = await host()

    await type(area, 'Hi {user} {args.2}')

    expect(document.body.querySelector('[data-template-warning]')).toBeNull()
  })
})

describe('the helper panel', () => {
  it('lists every helper with its description and sample', async () => {
    await host('', true)

    const row = document.body.querySelector('[data-helper-row="user.name"]') as HTMLElement

    expect(row.textContent).toContain('{user.name}')
    expect(row.textContent).toContain(i18n.global.t('template.helper.user_name'))
    expect(row.textContent).toContain('DukaSoft')
  })

  it('inserts at the caret the field remembered, even after focus moved away', async () => {
    const area: HTMLTextAreaElement = await host('ab cd', true)

    await type(area, 'ab cd', 2)

    area.blur()

    ;(document.body.querySelector('[data-helper-row="user"]') as HTMLElement).click()
    await flushPromises()

    expect(text.value).toBe('ab{user} cd')
    expect(area.selectionStart).toBe('ab{user}'.length)
    expect(document.activeElement).toBe(area)
  })

  it('replaces the selected text when something is selected', async () => {
    const area: HTMLTextAreaElement = await host('ab cd', true)

    await type(area, 'ab cd', 0)
    area.setSelectionRange(0, 2)

    area.dispatchEvent(new Event('select', { bubbles: true }))

    ;(document.body.querySelector('[data-helper-row="channel"]') as HTMLElement).click()
    await flushPromises()

    expect(text.value).toBe('{channel} cd')
  })
})
