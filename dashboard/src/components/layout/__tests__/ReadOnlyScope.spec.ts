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

import { mount, type VueWrapper } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';
import { defineComponent, h } from 'vue';

import ReadOnlyScope from '@/components/layout/ReadOnlyScope.vue';
import { useReadOnly } from '@/components/layout/readOnly';
import { i18n } from '@/i18n';

const reason = 'You can view this, but your role cannot change it.';

function controls() {
  return [h('input', { name: 'name' }), h('button', { type: 'button' }, 'Save')];
}

function scope(readOnly: boolean, props: Record<string, unknown> = {}): VueWrapper {
  return mount(ReadOnlyScope, {
    props: { readOnly, ...props },
    slots: { default: controls },
    attachTo: document.body,
    global: { plugins: [i18n] },
  });
}

describe('the read-only scope', () => {
  it('disables a native input and button inside a locked scope, so a click cannot fire', () => {
    const wrapper = scope(true);
    const press = { fired: 0 };
    const button = wrapper.get('button').element;

    button.addEventListener('click', () =>
      (press.fired += 1));

    button.click();

    expect(wrapper.get('fieldset').attributes('disabled')).toBeDefined();
    expect(wrapper.get('input').element.matches(':disabled')).toBe(true);
    expect(button.matches(':disabled')).toBe(true);
    expect(press.fired).toBe(0);
    wrapper.unmount();
  });

  it('puts the reason in the title of the locked block', () => {
    const wrapper = scope(true);

    expect(wrapper.get('fieldset').attributes('title')).toBe(reason);
    wrapper.unmount();
  });

  it('shows a given reason instead of the default', () => {
    const wrapper = scope(true, { reason: 'Only the broadcaster can change this.' });

    expect(wrapper.get('fieldset').attributes('title')).toBe(
      'Only the broadcaster can change this.',
    );

    wrapper.unmount();
  });

  it('leaves controls working and carries no title when unlocked', () => {
    const wrapper = scope(false);
    const press = { fired: 0 };
    const button = wrapper.get('button').element;

    button.addEventListener('click', () =>
      (press.fired += 1));

    button.click();

    expect(wrapper.get('fieldset').attributes('disabled')).toBeUndefined();
    expect(wrapper.get('fieldset').attributes('title')).toBeUndefined();
    expect(button.matches(':disabled')).toBe(false);
    expect(press.fired).toBe(1);
    wrapper.unmount();
  });

  it('keeps a nested scope locked when its parent is locked, whatever the child asks', () => {
    const inner = defineComponent({
      setup() {
        const locked = useReadOnly();

        return () =>
          h(ReadOnlyScope, { readOnly: false }, () => [
            h('output', { 'data-state': String(locked.value) }),
            h('button', { type: 'button' }, 'Inner'),
          ]);
      },
    });

    const wrapper = mount(
      defineComponent({
        render: () => h(ReadOnlyScope, { readOnly: true }, () =>
          h(inner)),
      }),
      { attachTo: document.body, global: { plugins: [i18n] } },
    );

    const fieldsets = wrapper.findAll('fieldset');

    expect(fieldsets).toHaveLength(2);
    expect(fieldsets[1]!.element.matches(':disabled')).toBe(true);
    expect(wrapper.get('[data-state]').attributes('data-state')).toBe('true');
    wrapper.unmount();
  });

  it('reports false from useReadOnly when no scope is above', () => {
    const seen: boolean[] = [];

    const probe = defineComponent({
      setup() {
        seen.push(useReadOnly().value);

        return () => h('div');
      },
    });

    mount(probe).unmount();

    expect(seen).toEqual([false]);
  });
});
