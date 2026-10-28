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

// OBS reports a timecode as "01:02:03.456"; the page shows whole seconds.
export function wholeSeconds(timecode: string | null | undefined): string {
  return (timecode ?? '').split('.')[0] ?? '';
}

// The share of frames that were skipped, as a percent with one decimal; zero frames read as 0.
export function skippedPercent(skipped: number | string, total: number | string): string {
  const all = Number(total);

  if (all <= 0)
    return '0.0';

  return ((Number(skipped) / all) * 100).toFixed(1);
}
