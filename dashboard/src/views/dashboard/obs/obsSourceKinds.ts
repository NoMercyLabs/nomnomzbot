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

import type { ObsInput, ObsScene } from '@/api/obs';

export type ScreenshotGroup = 'scenes' | 'browser' | 'captures' | 'media' | 'images';

export type ScreenshotSourceGroup = {
  group: ScreenshotGroup;
  names: string[];
};

const ORDER = ['scenes', 'browser', 'captures', 'media', 'images'] as const satisfies readonly ScreenshotGroup[];

// OBS input kinds that carry sound only (WASAPI, CoreAudio, PulseAudio, ALSA, JACK, OSS, sndio); OBS has no picture to capture.
const AUDIO_KIND = /^(wasapi|coreaudio|pulse|alsa|jack|oss|sndio)_|audio/;
const MEDIA_KIND = /^(ffmpeg|vlc)_source$/;
const IMAGE_KIND = /^(image_source|slideshow|color_source|text_)/;

// The group of one video input; a kind this page does not know counts as a capture.
function videoGroup(kind: string): ScreenshotGroup {
  if (kind === 'browser_source')
    return 'browser';

  if (MEDIA_KIND.test(kind))
    return 'media';

  return IMAGE_KIND.test(kind)
    ? 'images'
    : 'captures';
}

// Scenes first, then the video inputs by kind; each group sorted by name, empty groups left out, audio-only inputs never offered.
export function groupScreenshotSources(
  inputs: readonly Pick<ObsInput, 'name' | 'kind'>[],
  scenes: readonly Pick<ObsScene, 'name'>[],
): ScreenshotSourceGroup[] {
  const names: Record<ScreenshotGroup, string[]> = {
    scenes: scenes.map((scene) => scene.name),
    browser: [],
    captures: [],
    media: [],
    images: [],
  };

  for (const input of inputs) {
    if (!AUDIO_KIND.test(input.kind)) {
      names[videoGroup(input.kind)].push(input.name);
    }
  }

  return ORDER
    .filter((group) => names[group].length > 0)
    .map((group) => ({ group, names: names[group].sort((a, b) => a.localeCompare(b)) }));
}
