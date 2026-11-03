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

  // The default is the full width of the row; a width the page asks for (w-40 beside a label) must win, or the
  // control takes the whole row and squeezes the label to one letter per line.
  it.each([
    ['plain', false],
    ['searchable', true],
  ])('keeps the width a page asks for on a %s select', (_name, searchable) => {
    mount(OptionSelect, {
      attachTo: document.body,
      props: {
        options,
        modelValue: 'pro',
        searchable,
      },
      attrs: { 'data-hook': '', class: 'w-40' },
    });

    const trigger = document.body.querySelector<HTMLElement>('[data-hook]')!;

    expect(trigger.classList.contains('w-40')).toBe(true);
    expect(trigger.classList.contains('w-full')).toBe(false);
  });

  it('is the full row width when the page asks for no width', () => {
    mount(OptionSelect, {
      attachTo: document.body,
      props: { options, modelValue: 'pro' },
      attrs: { 'data-hook': '' },
    });

    expect(document.body.querySelector('[data-hook]')!.classList.contains('w-full')).toBe(true);
  });
});
