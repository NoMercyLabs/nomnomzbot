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
import { afterEach, describe, expect, it } from 'vitest';

import SearchField from '@/components/layout/SearchField.vue';

let mounted: VueWrapper | undefined;

afterEach(() => {
  mounted?.unmount();
  document.body.innerHTML = '';
});

function field(): HTMLInputElement {
  mounted = mount(SearchField, {
    attachTo: document.body,
    props: { placeholder: 'Search commands', modelValue: '' },
  });

  return mounted.find('input').element;
}

function press(key: string, target: EventTarget = document.body): KeyboardEvent {
  const event: KeyboardEvent = new KeyboardEvent('keydown', {
    key,
    bubbles: true,
    cancelable: true,
  });

  target.dispatchEvent(event);

  return event;
}

describe('the search field', () => {
  it('focuses on the key "/" and keeps the slash out of the box', () => {
    const input = field();

    const event = press('/');

    expect(document.activeElement).toBe(input);
    expect(event.defaultPrevented).toBe(true);
  });

  it('leaves "/" alone while a person types in another field', () => {
    const input = field();
    const other: HTMLInputElement = document.createElement('input');

    document.body.appendChild(other);
    other.focus();

    const event = press('/', other);

    expect(document.activeElement).toBe(other);
    expect(document.activeElement).not.toBe(input);
    expect(event.defaultPrevented).toBe(false);
  });

  it('emits what is typed', async () => {
    field();

    await mounted!.find('input').setValue('hel');

    expect(mounted!.emitted('update:modelValue')).toEqual([['hel']]);
  });

  it('names the box by its placeholder', () => {
    field();

    expect(mounted!.find('input').attributes('aria-label')).toBe('Search commands');
  });

  it('stops listening once removed', () => {
    const input = field();

    mounted!.unmount();
    mounted = undefined;

    press('/');

    expect(document.activeElement).not.toBe(input);
  });
});
