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

import { connectScopeSets } from '../integrations';

describe('connectScopeSets', () => {
  // !banger and the playlist pickers write to Spotify playlists: a connect without them fails every !banger.
  it('asks Spotify for playback and the playlists in one consent', () => {
    expect(connectScopeSets.spotify.split('+')).toEqual(['spotify.playback', 'spotify.library']);
  });
});
