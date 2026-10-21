// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { mount, type VueWrapper } from '@vue/test-utils';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { i18n } from '@/i18n';

import AuditFilters, { type AuditFilterValues } from '../components/AuditFilters.vue';
import en from '../i18n/en.json';

const empty: AuditFilterValues = {
  permission: '',
  outcome: '',
  principalId: '',
  targetBroadcasterId: '',
  from: '',
  to: '',
};

let mounted: VueWrapper | null = null;

function open(values: AuditFilterValues): VueWrapper {
  mounted = mount(AuditFilters, {
    attachTo: document.body,
    props: { modelValue: values },
    global: { plugins: [i18n] },
  });

  return mounted;
}

beforeEach(() => {
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

describe('the audit filters on a phone', () => {
  it('folds the six fields behind one Filters toggle that shows no badge while nothing is set', () => {
    const wrapper = open(empty);
    const toggle = wrapper.find('[data-filters-toggle]');

    expect(toggle.exists()).toBe(true);
    expect(toggle.classes()).toContain('md:hidden');
    expect(toggle.text()).toContain('Filters');
    expect(wrapper.find('[data-filters-count]').exists()).toBe(false);
  });

  it('counts the filters that are set in the badge', () => {
    const wrapper = open({
      ...empty,
      permission: 'tenant:suspend',
      outcome: 'Denied',
    });

    expect(wrapper.find('[data-filters-count]').text()).toBe('2');
  });

  it('keeps the fields open at md and up and closed below md until the toggle is pressed', async () => {
    const wrapper = open(empty);
    const fields = wrapper.find('[data-audit-filters]');

    expect(fields.attributes('data-state')).toBe('closed');
    expect(fields.classes()).toContain('max-md:data-[state=closed]:hidden');

    await wrapper.find('[data-filters-toggle]').trigger('click');

    expect(wrapper.find('[data-audit-filters]').attributes('data-state')).toBe('open');
    expect(wrapper.findAll('input')).toHaveLength(5);
  });
});
