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

import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import { Checkbox } from '@/components/ui/checkbox'
import { RadioGroup, RadioGroupItem } from '@/components/ui/radio-group'
import { Switch } from '@/components/ui/switch'

// Touch hit area: the visual size stays, an invisible ::after grows to 36 px or more on a coarse pointer.
// Switch 18.4 px high, checkbox and radio 16 px high: inset-y-3 adds 12 px on each side.
const TOUCH_AREA = 'pointer-coarse:after:-inset-y-3'

describe('touch hit area of toggle controls', () => {
  it('gives the switch a 36 px tall touch area', () => {
    const wrapper = mount(Switch)
    expect(wrapper.find('[data-slot="switch"]').classes()).toContain(TOUCH_AREA)
  })

  it('gives the checkbox a 36 px tall touch area', () => {
    const wrapper = mount(Checkbox)
    expect(wrapper.find('[data-slot="checkbox"]').classes()).toContain(TOUCH_AREA)
  })

  it('gives the radio item a 36 px tall touch area', () => {
    const item = mount({ components: { RadioGroup, RadioGroupItem }, template: '<RadioGroup><RadioGroupItem value="a" /></RadioGroup>' })
    expect(item.find('[data-slot="radio-group-item"]').classes()).toContain(TOUCH_AREA)
  })
})
