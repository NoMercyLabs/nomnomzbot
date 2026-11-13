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

import InlineNotice from '@/components/layout/InlineNotice.vue';

const TONES = ['info', 'warning', 'error', 'success', 'gated'] as const;

describe('inlineNotice', () => {
  it.each(TONES)('renders the %s tone with its role, data hook, title and slots', (tone) => {
    const wrapper = mount(InlineNotice, {
      props: { tone, title: 'Heads up' },
      attrs: { 'data-hook': 'x' },
      slots: { default: 'Body text', action: '<button data-act>Go</button>' },
    });

    expect(wrapper.attributes('role')).toBe(tone === 'error'
      ? 'alert'
      : 'status');

    expect(wrapper.attributes('data-hook')).toBe('x');
    expect(wrapper.attributes('data-tone')).toBe(tone);
    expect(wrapper.find('[data-notice-title]').text()).toBe('Heads up');
    expect(wrapper.text()).toContain('Body text');
    expect(wrapper.find('[data-act]').exists()).toBe(true);
  });

  it('lets the caller override the role', () => {
    const wrapper = mount(InlineNotice, { props: { tone: 'info', role: 'alert' } });

    expect(wrapper.attributes('role')).toBe('alert');
  });

  it('renders no title element and no action wrapper when none is given', () => {
    const wrapper = mount(InlineNotice, { props: { tone: 'info' }, slots: { default: 'Plain' } });

    expect(wrapper.find('[data-notice-title]').exists()).toBe(false);
    expect(wrapper.find('[data-notice-action]').exists()).toBe(false);
  });

  it('compact error is the plain destructive line without a border', () => {
    const wrapper = mount(InlineNotice, {
      props: { tone: 'error', compact: true },
      slots: { default: 'Failed' },
    });

    expect(wrapper.attributes('role')).toBe('alert');
    expect(wrapper.classes()).toContain('text-destructive');
    expect(wrapper.classes()).toContain('text-sm');
    expect(wrapper.classes()).not.toContain('border');
  });

  it('a bordered notice keeps the border and the rounded card shape', () => {
    const wrapper = mount(InlineNotice, { props: { tone: 'info' }, slots: { default: 'x' } });

    expect(wrapper.classes()).toContain('border');
    expect(wrapper.classes()).toContain('rounded-lg');
  });
});
