// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// m:ss for a song position.
export function formatClock(ms: number): string {
  const total = Math.max(0, Math.floor(ms / 1000))
  const seconds = total % 60

  return `${Math.floor(total / 60)}:${seconds.toString().padStart(2, '0')}`
}
