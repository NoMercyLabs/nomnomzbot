// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { FakeReply, Json } from '../fakeApi';

// The invented channel every preview belongs to. Its id is no real channel id.
export const sampleChannelId = 'sample-channel';

export const channelPath = `/api/v1/channels/${sampleChannelId}`;

// A moment a few minutes or hours before now, so a sample row reads as recent whenever the page is opened.
export function minutesAgo(minutes: number): string {
  return new Date(Date.now() - minutes * 60_000).toISOString();
}

// The bot wraps a single record as { data } and a list as { data, hasMore, total }.
export function one(data: Json): { status: string; data: Json } {
  return { status: 'success', data };
}

export function list(rows: readonly Json[]): {
  status: string;
  data: readonly Json[];
  hasMore: boolean;
  total: number;
} {
  return {
    status: 'success',
    data: rows,
    hasMore: false,
    total: rows.length,
  };
}

// A GET the sample answers with a fixed body.
export function get(path: string, body: Json | ((url: URL) => Json)) {
  return {
    method: 'GET',
    path,
    body: typeof body === 'function'
      ? body
      : () => body,
  } as const satisfies FakeReply;
}
