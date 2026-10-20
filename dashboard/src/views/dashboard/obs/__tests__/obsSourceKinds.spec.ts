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

import { groupScreenshotSources } from '../obsSourceKinds';

describe('groupScreenshotSources', () => {
  // OBS cannot render a still of an audio-only input, so none is offered.
  it('groups scenes and video inputs by kind and drops audio-only inputs', () => {
    const groups = groupScreenshotSources(
      [
        { name: 'Desktop Audio', kind: 'wasapi_output_capture' },
        { name: 'Mic/Aux', kind: 'wasapi_input_capture' },
        { name: 'Discord VC', kind: 'wasapi_process_output_capture' },
        { name: 'Chat', kind: 'browser_source' },
        { name: 'Camera', kind: 'dshow_input' },
        { name: 'Game', kind: 'game_capture' },
        { name: 'Intro', kind: 'ffmpeg_source' },
        { name: 'Logo', kind: 'image_source' },
      ],
      [{ name: 'Main' }],
    );

    expect(groups).toEqual([
      { group: 'scenes', names: ['Main'] },
      { group: 'browser', names: ['Chat'] },
      { group: 'captures', names: ['Camera', 'Game'] },
      { group: 'media', names: ['Intro'] },
      { group: 'images', names: ['Logo'] },
    ]);
  });

  it('leaves out a group with no source and sorts names inside a group', () => {
    const groups = groupScreenshotSources(
      [
        { name: 'Zed', kind: 'browser_source' },
        { name: 'Alpha', kind: 'browser_source' },
        { name: 'Mic', kind: 'coreaudio_input_capture' },
      ],
      [],
    );

    expect(groups).toEqual([{ group: 'browser', names: ['Alpha', 'Zed'] }]);
  });
});
