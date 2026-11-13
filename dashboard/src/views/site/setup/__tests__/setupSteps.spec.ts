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

import { describe, expect, it } from 'vitest';

import { hasCredentialForm } from '@/api/setup';

import BotAccountStep from '../components/BotAccountStep.vue';
import CredentialsStep from '../components/CredentialsStep.vue';
import { rendererFor, stepRenderers } from '../components/setupSteps';
import UnknownStep from '../components/UnknownStep.vue';

const serverKeys = ['twitch_app', 'platform_bot', 'spotify', 'discord', 'youtube'];

describe('the setup step renderer registry', () => {
  it('gives every step key the server sends its own renderer', () => {
    expect(rendererFor('twitch_app')).toBe(CredentialsStep);
    expect(rendererFor('spotify')).toBe(CredentialsStep);
    expect(rendererFor('discord')).toBe(CredentialsStep);
    expect(rendererFor('youtube')).toBe(CredentialsStep);
    expect(rendererFor('platform_bot')).toBe(BotAccountStep);
  });

  it('lists exactly the keys the server sends', () => {
    expect(Object.keys(stepRenderers).sort()).toEqual([...serverKeys].sort());
  });

  it('draws a credential form for every key the API can save credentials for', () => {
    serverKeys.filter(hasCredentialForm).forEach((key) => {
      expect(rendererFor(key)).toBe(CredentialsStep);
    });
  });

  it('falls back to the unknown step for a key it does not know, never a form', () => {
    expect(rendererFor('mystery_step')).toBe(UnknownStep);
    expect(rendererFor('')).toBe(UnknownStep);
    expect(rendererFor('toString')).toBe(UnknownStep);
  });
});
