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
import { afterEach, describe, expect, it, type MockInstance, vi } from 'vitest';
import { defineComponent, h, ref } from 'vue';

import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import { i18n } from '@/i18n';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogTitle,
  DialogTrigger,
} from '@/components/ui/dialog';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';

// Stoney 2026-10-19 sent the "Request rules" button with a focus ring after its panel closed ("double borders"):
// the closed overlay had handed the focus back to it. After a mouse or touch close the focus stays off the button;
// after a keyboard close it goes back, so a keyboard user keeps their place.
const Harness = defineComponent({
  setup: () => () =>
    h(Dialog, null, {
      default: () => [
        h(DialogTrigger, { 'data-opener': '' }, () => 'Request rules'),
        h(DialogContent, null, () => [
          h(DialogTitle, null, () => 'Request rules'),
          h(DialogDescription, null, () => 'Who may request songs.'),
        ]),
      ],
    }),
});

let mounted: VueWrapper | null = null;

afterEach(() => {
  mounted?.unmount();
  mounted = null;
});

async function settle(): Promise<void> {
  await flushPromises();
  await new Promise((resolve) => setTimeout(resolve, 0));
  await flushPromises();
}

function opener(): HTMLButtonElement {
  return document.body.querySelector<HTMLButtonElement>('[data-opener]')!;
}

function pointerPress(element: Element): void {
  element.dispatchEvent(new Event('pointerdown', { bubbles: true }));
  element.dispatchEvent(new MouseEvent('click', { bubbles: true }));
}

function press(key: string): void {
  document.activeElement!.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true }));
}

async function openWith(
  input: 'pointer' | 'keyboard',
  harness = Harness,
  slot = 'dialog-content',
): Promise<void> {
  mounted = mount(harness, { attachTo: document.body });
  await settle();
  opener().focus();

  if (input === 'pointer') {
    pointerPress(opener());
  } else {
    press('Enter');
    opener().click();
  }

  await settle();
  expect(document.body.querySelector(`[data-slot="${slot}"]`)).not.toBeNull();
}

// Stoney 2026-10-20: the account menu showed a focus ring on "Readability" right after a mouse click opened it. The
// overlay had moved the focus to its first button. After a mouse or touch open the focus goes to the panel itself;
// a text field first in the panel still takes it, so a person can type at once; the keyboard still lands on the
// first control.
function popoverHarness(firstTag: 'button' | 'input') {
  return defineComponent({
    setup: () => () =>
      h(Popover, null, {
        default: () => [
          h(PopoverTrigger, { 'data-opener': '' }, () => 'Account menu'),
          h(PopoverContent, null, () => [
            h(firstTag, { 'data-first': '' }, firstTag === 'button'
              ? 'Readability'
              : undefined),
            h('button', null, 'Sign out'),
          ]),
        ],
      }),
  });
}

function panel(): HTMLElement {
  return document.body.querySelector<HTMLElement>('[data-slot="popover-content"]')!;
}

function firstControl(): HTMLElement {
  return document.body.querySelector<HTMLElement>('[data-first]')!;
}

describe('focus when an overlay opens', () => {
  it('lands on the panel after a mouse open when a button comes first, so no ring shows on that button', async () => {
    await openWith('pointer', popoverHarness('button'), 'popover-content');

    expect(document.activeElement).toBe(panel());
    expect(document.activeElement).not.toBe(firstControl());
  });

  it('lands on the first button after a keyboard open, so a keyboard user starts at the top', async () => {
    await openWith('keyboard', popoverHarness('button'), 'popover-content');

    expect(document.activeElement).toBe(firstControl());
  });

  it('lands on the text field after a mouse open when a text field comes first, so a person can type at once', async () => {
    await openWith('pointer', popoverHarness('input'), 'popover-content');

    expect(document.activeElement).toBe(firstControl());
  });
});

// The shared confirm, opened by a real button the way Economy "Earning rules" opens it.
const ConfirmHarness = defineComponent({
  setup() {
    const open = ref(false);

    return () => [
      h('button', { 'data-opener': '', onClick: () => (open.value = true) }, 'Delete rule'),
      h(ConfirmDialog, {
        open: open.value,
        'onUpdate:open': (value: boolean) => (open.value = value),
        title: 'Delete rule?',
        action: 'Delete rule',
      }),
    ];
  },
});

type QuietOptions = FocusOptions & { focusVisible?: boolean };

function focusCallsOn(spy: MockInstance, element: Element): QuietOptions[] {
  return spy.mock.contexts
    .map((context, index) => ({ context, options: spy.mock.calls[index]?.[0] as QuietOptions }))
    .filter((call) => call.context === element)
    .map((call) => call.options);
}

describe('focus after the confirm closes with Escape', () => {
  it('returns to the button without a focus ring when a mouse opened it', async () => {
    mounted = mount(ConfirmHarness, { attachTo: document.body, global: { plugins: [i18n] } });
    await settle();
    opener().focus();
    pointerPress(opener());
    await settle();
    expect(document.body.querySelector('[role="alertdialog"]')).not.toBeNull();

    const spy = vi.spyOn(HTMLElement.prototype, 'focus');

    press('Escape');
    await settle();
    const calls = focusCallsOn(spy, opener());

    spy.mockRestore();

    expect(document.body.querySelector('[role="alertdialog"]')).toBeNull();
    expect(document.activeElement).toBe(opener());
    expect(calls.at(-1)).toMatchObject({ focusVisible: false });
  });

  it('returns to the button with its ring when the keyboard opened it', async () => {
    mounted = mount(ConfirmHarness, { attachTo: document.body, global: { plugins: [i18n] } });
    await settle();
    opener().focus();
    press('Enter');
    opener().click();
    await settle();
    expect(document.body.querySelector('[role="alertdialog"]')).not.toBeNull();

    const spy = vi.spyOn(HTMLElement.prototype, 'focus');

    press('Escape');
    await settle();
    const calls = focusCallsOn(spy, opener());

    spy.mockRestore();

    expect(document.activeElement).toBe(opener());
    expect(calls.every((options) => options?.focusVisible !== false)).toBe(true);
  });
});

describe('focus after an overlay closes', () => {
  it('goes back to the button after a mouse close, without a focus ring', async () => {
    await openWith('pointer');

    const spy = vi.spyOn(HTMLElement.prototype, 'focus');

    pointerPress(document.body.querySelector('[data-slot="dialog-close"]')!);
    await settle();
    const calls = focusCallsOn(spy, opener());

    spy.mockRestore();

    expect(document.body.querySelector('[data-slot="dialog-content"]')).toBeNull();
    expect(document.activeElement).toBe(opener());
    expect(calls.at(-1)).toMatchObject({ focusVisible: false });
  });

  it('goes back to the button that opened it after a keyboard close', async () => {
    await openWith('keyboard');

    press('Escape');
    await settle();

    expect(document.body.querySelector('[data-slot="dialog-content"]')).toBeNull();
    expect(document.activeElement).toBe(opener());
  });
});
