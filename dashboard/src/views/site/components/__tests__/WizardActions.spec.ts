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

import { mount } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';

import WizardActions from '../WizardActions.vue';

describe('the wizard actions row', () => {
  it('puts Back on the left and Next on the right of one row', () => {
    const wrapper = mount(WizardActions, {
      slots: {
        back: '<button id="back">Back</button>',
        next: '<button id="next">Next</button>',
      },
    });
    const row = wrapper.get('[data-slot="wizard-actions"]');
    const ids = row.findAll('button').map((b) => b.attributes('id'));

    expect(ids).toEqual(['back', 'next']);
    expect(row.classes()).toContain('justify-between');
    expect(row.classes()).not.toContain('flex-col');
  });

  it('keeps Next on the right when there is no Back', () => {
    const wrapper = mount(WizardActions, {
      slots: { next: '<button id="next">Next</button>' },
    });
    const row = wrapper.get('[data-slot="wizard-actions"]');

    expect(row.findAll('button')).toHaveLength(1);
    expect(row.classes()).toContain('justify-between');
  });
});
