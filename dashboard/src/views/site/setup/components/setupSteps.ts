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

import type { Component } from 'vue';

import BotAccountStep from './BotAccountStep.vue';
import CredentialsStep from './CredentialsStep.vue';
import UnknownStep from './UnknownStep.vue';

// The step keys the bot sends today, each with the component that draws it. A new bot step is one new line here.
export type KnownStepKey = 'twitch_app' | 'platform_bot' | 'spotify' | 'discord' | 'youtube';

export const stepRenderers = {
  twitch_app: CredentialsStep,
  platform_bot: BotAccountStep,
  spotify: CredentialsStep,
  discord: CredentialsStep,
  youtube: CredentialsStep,
} as const satisfies Record<KnownStepKey, Component>;

function isKnownStep(key: string): key is KnownStepKey {
  return Object.hasOwn(stepRenderers, key);
}

// A key the registry does not know draws UnknownStep, never a wrong form.
export function rendererFor(key: string): Component {
  return isKnownStep(key)
    ? stepRenderers[key]
    : UnknownStep;
}
