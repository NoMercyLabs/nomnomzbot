// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
