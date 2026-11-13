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
import type { AcceptableValue } from 'reka-ui';
import { afterEach, beforeAll, describe, expect, it, type MockInstance, vi } from 'vitest';
import { defineComponent, h, ref } from 'vue';

import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';

// Fault 25b: a Select opened with the mouse and closed with Escape handed the focus back to its trigger with a
// focus ring, because Escape is a key and the browser then draws the ring. A mouse open now returns the focus
// quietly; a keyboard open keeps the ring, so a keyboard user still sees where they are.
const Harness = defineComponent({
  setup: () => {
    const voice = ref('ava');

    return () =>
      h(Select, { 'modelValue': voice.value, 'onUpdate:modelValue': (value: AcceptableValue) => (voice.value = String(value)) }, {
        default: () => [
          h(SelectTrigger, { 'data-opener': '', 'aria-label': 'Voice' }, () => h(SelectValue)),
          h(SelectContent, null, () => [
            h(SelectItem, { value: 'ava' }, () => 'Ava'),
            h(SelectItem, { value: 'brian' }, () => 'Brian'),
          ]),
        ],
      });
  },
});

type QuietOptions = FocusOptions & { focusVisible?: boolean };

let mounted: VueWrapper | null = null;

beforeAll(() => {
  Element.prototype.hasPointerCapture = (): boolean => false;
  Element.prototype.releasePointerCapture = (): void => undefined;
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
});

async function settle(): Promise<void> {
  await flushPromises();
  await new Promise((resolve) => setTimeout(resolve, 0));
  await flushPromises();
}

function trigger(): HTMLButtonElement {
  return document.body.querySelector<HTMLButtonElement>('[data-opener]')!;
}

function listbox(): Element | null {
  return document.body.querySelector('[role="listbox"]');
}

function pointer(type: 'pointerdown' | 'pointerup'): void {
  const init = {
    bubbles: true,
    button: 0,
    pointerType: 'mouse',
  };

  trigger().dispatchEvent(new PointerEvent(type, init));
}

function press(key: string): void {
  document.activeElement!.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true }));
}

function focusCallsOn(spy: MockInstance, element: Element): QuietOptions[] {
  return spy.mock.contexts
    .map((context, index) => ({ context, options: spy.mock.calls[index]?.[0] as QuietOptions }))
    .filter((call) => call.context === element)
    .map((call) => call.options);
}

async function closeWithEscape(): Promise<QuietOptions[]> {
  const spy = vi.spyOn(HTMLElement.prototype, 'focus');

  press('Escape');
  await settle();
  const calls = focusCallsOn(spy, trigger());

  spy.mockRestore();

  return calls;
}

describe('focus after a select closes with Escape', () => {
  it('returns to the trigger without a focus ring when a mouse opened it', async () => {
    mounted = mount(Harness, { attachTo: document.body });
    await settle();
    pointer('pointerdown');
    pointer('pointerup');
    await settle();
    expect(listbox()).not.toBeNull();

    const calls = await closeWithEscape();

    expect(listbox()).toBeNull();
    expect(document.activeElement).toBe(trigger());
    expect(calls.at(-1)).toMatchObject({ focusVisible: false });
  });

  it('returns to the trigger with its ring when the keyboard opened it', async () => {
    mounted = mount(Harness, { attachTo: document.body });
    await settle();
    trigger().focus();
    press('Enter');
    await settle();
    expect(listbox()).not.toBeNull();

    const calls = await closeWithEscape();

    expect(listbox()).toBeNull();
    expect(document.activeElement).toBe(trigger());
    expect(calls.length).toBeGreaterThan(0);
    expect(calls.every((options) => options?.focusVisible !== false)).toBe(true);
  });
});
