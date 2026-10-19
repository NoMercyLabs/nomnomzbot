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
import { defineComponent, h } from 'vue';

import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogTitle,
  DialogTrigger,
} from '@/components/ui/dialog';

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

async function openWith(input: 'pointer' | 'keyboard'): Promise<void> {
  mounted = mount(Harness, { attachTo: document.body });
  await settle();
  opener().focus();

  if (input === 'pointer') {
    pointerPress(opener());
  } else {
    press('Enter');
    opener().click();
  }

  await settle();
  expect(document.body.querySelector('[data-slot="dialog-content"]')).not.toBeNull();
}

describe('focus after an overlay closes', () => {
  it('stays off the button that opened it after a mouse close, so no focus ring is left there', async () => {
    await openWith('pointer');

    pointerPress(document.body.querySelector('[data-slot="dialog-close"]')!);
    await settle();

    expect(document.body.querySelector('[data-slot="dialog-content"]')).toBeNull();
    expect(document.activeElement).not.toBe(opener());
  });

  it('goes back to the button that opened it after a keyboard close', async () => {
    await openWith('keyboard');

    press('Escape');
    await settle();

    expect(document.body.querySelector('[data-slot="dialog-content"]')).toBeNull();
    expect(document.activeElement).toBe(opener());
  });
});
