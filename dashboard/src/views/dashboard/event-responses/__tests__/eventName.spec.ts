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

import { nameOf } from '../components/eventResponseDraft';

describe('nameOf', () => {
  it('reads a Twitch event type in plain words', () => {
    expect(nameOf('channel.subscription.gift')).toBe('Subscription gift');
  });

  it('turns an OBS event type into words with the product name', () => {
    expect(nameOf('obs.CurrentProgramSceneChanged')).toBe('OBS current program scene changed');
    expect(nameOf('obs.RecordStateChanged')).toBe('OBS record state changed');
    expect(nameOf('obs.VendorEvent')).toBe('OBS vendor');
  });

  it('turns a VTube Studio event type into words with the product name', () => {
    expect(nameOf('vts.HotkeyTriggeredEvent')).toBe('VTube Studio hotkey triggered');
    expect(nameOf('vts.ModelClickedEvent')).toBe('VTube Studio model clicked');
  });
});
