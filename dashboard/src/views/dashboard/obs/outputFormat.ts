// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
