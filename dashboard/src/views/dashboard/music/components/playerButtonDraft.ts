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
