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

import { connectScopeSets } from '../integrations';

describe('connectScopeSets', () => {
  // !banger and the playlist pickers write to Spotify playlists: a connect without them fails every !banger.
  it('asks Spotify for playback and the playlists in one consent', () => {
    expect(connectScopeSets.spotify.split('+')).toEqual(['spotify.playback', 'spotify.library']);
  });
});
