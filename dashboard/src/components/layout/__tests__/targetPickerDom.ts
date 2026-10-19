// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
