// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { PickList, PickListDraft } from '@/api/pickLists';

// The most the server keeps: a name, a description and the entries of one list.
export const maxNameLength = 100;
export const maxDescriptionLength = 500;
export const maxItems = 500;
export const maxItemLength = 500;

// What the dialog fields hold: the entries as text, one per line.
export type PickListForm = {
  name: string;
  description: string;
  items: string;
};

export type PickListProblem = 'nameRequired' | 'nameTooLong' | 'descriptionTooLong' | 'tooManyItems' | 'itemTooLong';

export function formFromPickList(list: PickList | null) {
  return {
    name: list?.name ?? '',
    description: list?.description ?? '',
    items: (list?.items ?? []).join('\n'),
  } as const satisfies PickListForm;
}

// One entry per non-empty line, trimmed.
export function itemsOf(text: string): string[] {
  return text
    .split('\n')
    .map((line) => line.trim())
    .filter((line) => line !== '');
}

export function formProblem(form: PickListForm): PickListProblem | undefined {
  const items = itemsOf(form.items);

  if (form.name.trim() === '')
    return 'nameRequired';

  if (form.name.trim().length > maxNameLength)
    return 'nameTooLong';

  if (form.description.trim().length > maxDescriptionLength)
    return 'descriptionTooLong';

  if (items.length > maxItems)
    return 'tooManyItems';

  if (items.some((item) => item.length > maxItemLength))
    return 'itemTooLong';

  return undefined;
}

export function draftFromForm(form: PickListForm) {
  const description = form.description.trim();

  return {
    name: form.name.trim(),
    description: description === ''
      ? null
      : description,
    items: itemsOf(form.items),
  } as const satisfies PickListDraft;
}
