// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { afterEach, describe, expect, it } from 'vitest';

import OptionSelect from '@/components/layout/OptionSelect.vue';

const options = [
  { value: '', label: 'No tier' },
  { value: 'pro', label: 'Pro' },
  { value: 'team', label: 'Team' },
];

afterEach(() => {
  document.body.innerHTML = '';
});

function mountSelect(props: Record<string, unknown> = {}): VueWrapper {
  return mount(OptionSelect, {
    attachTo: document.body,
    props: {
      options,
      modelValue: 'pro',
      placeholder: 'Choose',
      ...props,
    },
    attrs: { 'data-hook': '' },
  });
}

async function openPlain(): Promise<void> {
  Element.prototype.hasPointerCapture = (): boolean => false;
  Element.prototype.releasePointerCapture = (): void => undefined;

  document.body.querySelector<HTMLElement>('[data-hook]')!.dispatchEvent(
    new PointerEvent('pointerdown', {
      bubbles: true,
      button: 0,
      pointerType: 'mouse',
    }),
  );

  await flushPromises();
}

async function chooseOption(label: string): Promise<void> {
  const option = Array.from(document.body.querySelectorAll<HTMLElement>('[role="option"]')).find(
    (node) => node.textContent?.trim() === label,
  )!;

  option.dispatchEvent(
    new PointerEvent('pointerup', {
      bubbles: true,
      button: 0,
      pointerType: 'mouse',
    }),
  );

  option.click();
  await flushPromises();
}

describe('OptionSelect', () => {
  it('draws no native select and puts the page hook on the trigger', () => {
    mountSelect();

    expect(document.body.querySelector('select')).toBeNull();
    expect(document.body.querySelector('[data-hook]')?.getAttribute('role')).toBe('combobox');
  });

  it('shows the label of the chosen value, and the placeholder when nothing matches', () => {
    mountSelect();
    expect(document.body.querySelector('[data-hook]')?.textContent).toContain('Pro');

    document.body.innerHTML = '';
    mountSelect({ modelValue: 'gone' });
    expect(document.body.querySelector('[data-hook]')?.textContent).toContain('Choose');
  });

  it('emits the value of the picked option, and an empty value for a choice whose value is empty', async () => {
    const wrapper = mountSelect();

    await openPlain();
    await chooseOption('Team');
    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual(['team']);

    await openPlain();
    await chooseOption('No tier');
    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual(['']);
  });

  it('filters a searchable list as the person types and emits the picked value', async () => {
    const wrapper = mountSelect({
      searchable: true,
      searchPlaceholder: 'Find',
      emptyText: 'None found',
    });

    document.body.querySelector<HTMLElement>('[data-hook]')!.click();
    await flushPromises();

    const input = document.body.querySelector<HTMLInputElement>('input')!;

    input.value = 'tea';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await flushPromises();

    const items = Array.from(document.body.querySelectorAll<HTMLElement>('[data-option]'));

    expect(items.map((node) => node.textContent?.trim())).toEqual(['Team']);

    items[0]!.click();
    await flushPromises();
    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual(['team']);
  });
});
