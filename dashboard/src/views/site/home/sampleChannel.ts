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

// The invented channel the public home page shows. All of it is sample data for a made-up channel; none of it is
// read from a real channel, and the page says so next to every preview. The records the previews read live in
// preview/replies and use this channel; no preview shows a number from anywhere else.
export const sampleChannel = {
  isSample: true,
  handle: 'crumb_tv',
  viewers: 1204,
  uptimeMinutes: 134,
  // How long ago the sample install was set up. A record a fresh install seeds (commands, pipelines, widgets)
  // is dated from this moment, so the lists read as a few weeks old instead of "just now".
  installedMinutesAgo: 60 * 24 * 21,
} as const;
