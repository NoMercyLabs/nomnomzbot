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
