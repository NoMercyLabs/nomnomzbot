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

import type { SoundClip, SoundClipChange } from '@/api/soundClips';

// The most the server keeps for a chat trigger word, and the loudest a clip plays.
export const maxTriggerLength = 50;
export const maxVolume = 100;

// What the add dialog holds.
export type AddForm = {
  file: File | null;
  name: string;
  displayName: string;
  defaultVolume: number;
};

// What the edit dialog holds: numbers as numbers, a blank trigger word as an empty string.
export type EditForm = {
  displayName: string;
  defaultVolume: number;
  cooldownSeconds: number;
  minPermissionLevel: string;
  triggerWord: string;
};

export type SoundClipProblem =
  | 'fileRequired' |
  'nameRequired' |
  'displayNameRequired' |
  'volumeRange' |
  'cooldownNegative' |
  'triggerTooLong' |
  'triggerHasSpace';

export function blankAddForm() {
  return {
    file: null,
    name: '',
    displayName: '',
    defaultVolume: 80,
  } as const satisfies AddForm;
}

export function editFormFrom(clip: SoundClip) {
  return {
    displayName: clip.displayName,
    defaultVolume: Number(clip.defaultVolume),
    cooldownSeconds: Number(clip.cooldownSeconds),
    minPermissionLevel: clip.minPermissionLevel,
    triggerWord: clip.triggerWord ?? '',
  } as const satisfies EditForm;
}

function volumeProblem(volume: number): SoundClipProblem | undefined {
  return Number.isInteger(volume) && volume >= 0 && volume <= maxVolume
    ? undefined
    : 'volumeRange';
}

export function addProblem(form: AddForm): SoundClipProblem | undefined {
  if (form.file === null)
    return 'fileRequired';

  if (form.name.trim() === '')
    return 'nameRequired';

  if (form.displayName.trim() === '')
    return 'displayNameRequired';

  return volumeProblem(form.defaultVolume);
}

export function editProblem(form: EditForm): SoundClipProblem | undefined {
  const trigger = form.triggerWord.trim();

  if (form.displayName.trim() === '')
    return 'displayNameRequired';

  if (volumeProblem(form.defaultVolume) !== undefined)
    return 'volumeRange';

  if (!Number.isInteger(form.cooldownSeconds) || form.cooldownSeconds < 0)
    return 'cooldownNegative';

  if (trigger.length > maxTriggerLength)
    return 'triggerTooLong';

  return /\s/.test(trigger)
    ? 'triggerHasSpace'
    : undefined;
}

// An edit keeps the clip's on/off state; the row switch is what changes it.
export function changeFromForm(form: EditForm, isEnabled: boolean) {
  const trigger = form.triggerWord.trim();

  return {
    displayName: form.displayName.trim(),
    defaultVolume: form.defaultVolume,
    isEnabled,
    cooldownSeconds: form.cooldownSeconds,
    minPermissionLevel: form.minPermissionLevel,
    triggerWord: trigger === ''
      ? null
      : trigger,
  } as const satisfies SoundClipChange;
}
