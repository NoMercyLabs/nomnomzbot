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

import { durationText } from '@/lib/time';

// How long a TTS clip took to synthesize: "11.3 sec". The bot sends no time for a clip written before it kept
// the timing, and no average when no clip has one, so a missing time reads as the given dash, never as 0.
export function synthesisText(
  milliseconds: number | string | null | undefined,
  locale: string,
  none: string,
): string {
  if (milliseconds === null || milliseconds === undefined)
    return none;

  return durationText(Number(milliseconds), locale);
}
