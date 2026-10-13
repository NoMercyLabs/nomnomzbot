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

import InlineFlow from '@/components/layout/InlineFlow.vue'
import type { InlineFlowKind } from '@/composables/useInlineFlow'
import { i18n } from '@/i18n'

let mounted: VueWrapper | null = null

afterEach(() => {
  mounted?.unmount()
  mounted = null
  document.body.innerHTML = ''
  vi.useRealTimers()
})

function flow(props: Record<string, unknown>): VueWrapper {
  mounted = mount(InlineFlow, {
    props: { label: 'Clear the queue', ...props } as { kind: InlineFlowKind; label: string },
    slots: { default: '<button type="button" data-opener>Clear queue</button>' },
    attachTo: document.body,
    global: { plugins: [i18n] },
  })

  return mounted
}

function step(): string | undefined {
  const root: HTMLElement | null = document.body.querySelector('[data-slot="inline-flow"]')

  return root?.dataset.step
}

function opener(): HTMLButtonElement {
  return document.body.querySelector('[data-opener]') as HTMLButtonElement
}

function control(act: string): HTMLButtonElement | null {
  return document.body.querySelector(`[data-flow-act="${act}"]`)
}

function press(act: string): void {
  control(act)?.click()
}

function box(): HTMLElement | null {
  return document.body.querySelector('[data-slot="inline-flow-box"]')
}

describe('the inline flow', () => {
  it('grows from the left edge, or from the right edge for a group at the end of a row', async () => {
    flow({ kind: 'note', text: 'Opens Spotify.' })
    opener().click()
    await flushPromises()
    expect(box()?.classList.contains('left-0')).toBe(true)
    mounted?.unmount()

    flow({ kind: 'note', text: 'Opens Spotify.', right: true })
    opener().click()
    await flushPromises()
    expect(box()?.classList.contains('right-0')).toBe(true)
    expect(box()?.classList.contains('left-0')).toBe(false)
  })

  it('confirm asks first and runs the action only on the second press', async () => {
    const action = vi.fn<() => void>()

    flow({
      kind: 'confirm',
      ask: 'Clear 12 songs?',
      yes: 'Clear',
      result: 'Queue cleared.',
      action,
    })

    opener().click()
    await flushPromises()

    expect(step()).toBe('ask')
    expect(action).not.toHaveBeenCalled()
    expect(box()?.textContent).toContain('Clear 12 songs?')

    press('ok')
    await flushPromises()

    expect(action).toHaveBeenCalledTimes(1)
    expect(step()).toBe('done')
    expect(box()?.textContent).toContain('Queue cleared.')
  })

  it('Esc backs out of a confirm without running the action and gives back the opener', async () => {
    const action = vi.fn<() => void>()

    flow({ kind: 'confirm', ask: 'Clear 12 songs?', result: 'Queue cleared.', action })
    opener().click()
    await flushPromises()

    control('back')?.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
    await flushPromises()

    expect(action).not.toHaveBeenCalled()
    expect(step()).toBe('idle')
    expect(box()).toBeNull()
    expect(document.activeElement).toBe(opener())
  })

  it('Cancel backs out of a confirm without running the action', async () => {
    const action = vi.fn<() => void>()

    flow({ kind: 'confirm', ask: 'Clear 12 songs?', action })
    opener().click()
    await flushPromises()

    press('back')
    await flushPromises()

    expect(action).not.toHaveBeenCalled()
    expect(step()).toBe('idle')
  })

  it('run shows pending while the action works, then the result', async () => {
    let finish: () => void = () => {}
    const action = vi.fn<() => Promise<void>>(
      () =>
        new Promise<void>((resolve: () => void) => {
          finish = resolve
        }),
    )

    flow({ kind: 'run', text: 'Playing the sound', result: 'Sound played.', action })

    opener().click()
    await flushPromises()

    expect(action).toHaveBeenCalledTimes(1)
    expect(step()).toBe('pending')
    expect(box()?.textContent).toContain('Playing the sound')
    expect(box()?.textContent).not.toContain('Sound played.')

    finish()
    await flushPromises()

    expect(step()).toBe('done')
    expect(box()?.textContent).toContain('Sound played.')
  })

  it('apply runs at once with no question', async () => {
    const action = vi.fn<() => void>()

    flow({ kind: 'apply', result: 'Saved.', action })

    opener().click()
    await flushPromises()

    expect(action).toHaveBeenCalledTimes(1)
    expect(step()).toBe('done')
  })

  it('Undo calls the undo handler and restores the start state', async () => {
    const action = vi.fn<() => void>()
    const revert = vi.fn<() => void>()

    flow({ kind: 'apply', result: 'Saved.', action, revert })
    opener().click()
    await flushPromises()
    expect(control('undo')).not.toBeNull()

    press('undo')
    await flushPromises()

    expect(revert).toHaveBeenCalledTimes(1)
    expect(step()).toBe('idle')
    expect(box()).toBeNull()
    expect(opener().closest('[data-slot="inline-flow-opener"]')?.hasAttribute('inert')).toBe(false)
  })

  it('the Undo window ends: after undoMs the result and Undo are gone and the handler can no longer run', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] })
    const revert = vi.fn<() => void>()

    flow({ kind: 'apply', result: 'Saved.', action: vi.fn<() => void>(), revert, undoMs: 5000 })
    opener().click()
    await flushPromises()

    await vi.advanceTimersByTimeAsync(4999)
    expect(step()).toBe('done')
    expect(control('undo')).not.toBeNull()

    await vi.advanceTimersByTimeAsync(2)
    await flushPromises()

    expect(step()).toBe('idle')
    expect(control('undo')).toBeNull()
    expect(revert).not.toHaveBeenCalled()
  })

  it('shows a failure, not a result, when the action rejects', async () => {
    flow({
      kind: 'apply',
      result: 'Saved.',
      action: vi.fn<() => Promise<void>>().mockRejectedValue(new Error('boom')),
    })

    opener().click()
    await flushPromises()

    expect(step()).toBe('failed')
    expect(box()?.textContent).toContain('That did not work.')
    expect(box()?.textContent).not.toContain('Saved.')
    expect(control('undo')).toBeNull()
  })

  it('shows the reason the bot gave when the page passes one for a failure', async () => {
    flow({
      kind: 'run',
      result: 'Sent.',
      failure: 'No such event type.',
      action: vi.fn<() => Promise<void>>().mockRejectedValue(new Error('No such event type.')),
    })

    opener().click()
    await flushPromises()

    expect(step()).toBe('failed')
    expect(box()?.textContent).toContain('No such event type.')
    expect(box()?.textContent).not.toContain('That did not work.')
  })

  it('note shows its line and Done, and has no action to run', async () => {
    flow({ kind: 'note', text: 'This page comes later.' })

    opener().click()
    await flushPromises()

    expect(step()).toBe('done')
    expect(box()?.textContent).toContain('This page comes later.')
    expect(control('undo')).toBeNull()

    press('back')
    await flushPromises()
    expect(step()).toBe('idle')
  })
})
