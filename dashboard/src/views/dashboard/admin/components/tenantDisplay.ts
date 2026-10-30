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

// How a tenant looks wherever the admin panel shows one: the list row and the detail panel read the same
// platform line. A platform shows by its name, never its key.
export function platformLine(
  platforms: readonly string[],
  translate: (key: string) => string,
): string {
  return platforms
    .map((platform) => translate(`common.provider.${platform}`))
    .join(', ');
}
