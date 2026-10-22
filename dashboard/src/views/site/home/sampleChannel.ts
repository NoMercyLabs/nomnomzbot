// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// The invented channel the public home page shows. All of it is sample data for a made-up channel; none of it is
// read from a real channel, and the page says so next to every preview. The records the previews read live in
// preview/replies and use this channel; no preview shows a number from anywhere else.
export const sampleChannel = {
  isSample: true,
  handle: 'crumb_tv',
  viewers: 1204,
  uptimeMinutes: 134,
} as const;
