// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { MediaShareRequest } from '@/api/mediaShare';

// The queue reads in the order the streamer acts on it: what plays now, what waits for a decision,
// what plays next, and last the finished items.
// The playing item sits in the player card above the queue, so no section lists it.
export const queueSectionKeys = ['pending', 'approved', 'history'] as const;

export type QueueSectionKey = (typeof queueSectionKeys)[number];

export type QueueSection = {
  key: QueueSectionKey;
  rows: MediaShareRequest[];
};

function sectionOf(status: string): QueueSectionKey | null {
  if (status === 'playing')
    return null;

  if (status === 'pending' || status === 'approved')
    return status;

  return 'history';
}

// Sections with no item are left out, so the page never shows an empty heading.
export function groupQueue(rows: readonly MediaShareRequest[]): QueueSection[] {
  return queueSectionKeys
    .map((key) => ({
      key,
      rows: rows.filter((row): boolean => sectionOf(row.status) === key),
    }) as const satisfies QueueSection)
    .filter((section): boolean => section.rows.length > 0);
}
