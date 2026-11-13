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

// Reads a rendered form the way a screen reader does: every FieldLabel must name a control through `for`,
// and that control must be a real element inside the same root. The specs of the Field migration share it.

// The FieldLabels of a rendered form (the label element of the Field primitive).
export function fieldLabels(root: ParentNode): HTMLLabelElement[] {
  return Array.from(root.querySelectorAll<HTMLLabelElement>('label[data-slot="field-label"]'));
}

// The text of each FieldLabel whose `for` is missing or points at no element inside the root.
export function unlinkedLabels(root: ParentNode): string[] {
  return fieldLabels(root)
    .filter((label) => {
      const target = label.getAttribute('for');

      return !target || root.querySelector(`[id="${target}"]`) === null;
    })
    .map((label) => label.textContent?.trim() ?? '');
}

// The message lines of the FieldErrors of a rendered form.
export function fieldErrors(root: ParentNode): string[] {
  return Array.from(root.querySelectorAll('[data-slot="field-error"]')).map(
    (line) => line.textContent?.trim() ?? '',
  );
}
