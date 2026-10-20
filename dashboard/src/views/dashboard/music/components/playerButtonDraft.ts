// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// The list the editor changes before it is saved. Each draft button carries a local key so a row keeps its
// place on screen while it moves; the key never reaches the bot.
import { buttonProblems, type PlayerButton } from '@/api/playerButtons';

export type DraftButton = PlayerButton & { key: string };

export function toDraft(buttons: PlayerButton[]): DraftButton[] {
  return buttons.map((button) =>
    ({
      ...button,
      parameters: { ...button.parameters },
      key: crypto.randomUUID(),
    }));
}

// A button the person just added: the bot gives it its id when the list is saved.
export function blankButton(): DraftButton {
  return {
    id: '',
    key: crypto.randomUUID(),
    label: '',
    icon: 'lightning',
    actionType: '',
    parameters: {},
  };
}

export function toSaved(drafts: DraftButton[]): PlayerButton[] {
  return drafts.map(({ id, label, icon, actionType, parameters }) =>
    ({
      id,
      label: label.trim(),
      icon,
      actionType,
      parameters,
    }));
}

export function hasProblems(drafts: DraftButton[]): boolean {
  return drafts.some((button) =>
    Object.keys(buttonProblems(button)).length > 0);
}
