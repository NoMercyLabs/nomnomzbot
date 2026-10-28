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

// How a view test drives the shared TargetPicker: open its list, then click the row that shows the text.
import { flushPromises } from '@vue/test-utils';

export async function openTargetPicker(root: ParentNode = document.body): Promise<void> {
  root.querySelector<HTMLElement>('[data-target-picker] [role="combobox"]')!.click();

  await flushPromises();
}

export function targetRow(text: string): HTMLElement | undefined {
  return Array.from(document.body.querySelectorAll<HTMLElement>('[data-slot="command-item"]'))
    .find((candidate) => candidate.textContent?.includes(text));
}

export async function chooseTarget(text: string): Promise<void> {
  targetRow(text)!.click();

  await flushPromises();
}
