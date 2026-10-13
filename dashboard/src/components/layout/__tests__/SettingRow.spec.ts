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
import { describe, expect, it } from 'vitest'

import SettingRow from '@/components/layout/SettingRow.vue'

function row(modelValue: boolean, disabled: boolean = false): VueWrapper {
  return mount(SettingRow, {
    attachTo: document.body,
    props: { title: 'Speak with TTS', description: 'Read aloud on stream.', modelValue, disabled },
  })
}

describe('a setting row', () => {
  it('turns the setting on when the text of the row is pressed', async () => {
    const wrapper = row(false)

    await wrapper.find('.text-muted-foreground').trigger('click')

    expect(wrapper.emitted('update:modelValue')).toEqual([[true]])
  })

  it('turns the setting off when the switch itself is pressed, once', async () => {
    const wrapper = row(true)

    await wrapper.find('[role="switch"]').trigger('click')

    expect(wrapper.emitted('update:modelValue')).toEqual([[false]])
  })

  it('names the switch by the title, so a reader announces what it sets', () => {
    const wrapper = row(false)
    const labeledBy: string = wrapper.find('[role="switch"]').attributes('aria-labelledby')!

    expect(document.getElementById(labeledBy)?.textContent).toBe('Speak with TTS')
  })

  it('changes nothing while disabled', async () => {
    const wrapper: VueWrapper = row(false, true)

    await wrapper.find('.text-muted-foreground').trigger('click')

    expect(wrapper.emitted('update:modelValue')).toBeUndefined()
  })
})
