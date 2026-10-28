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

import { sampleChannel } from '../../sampleChannel';
import type { FakeReply, Json } from '../fakeApi';

// The invented channel every preview belongs to. Its id is no real channel id.
export const sampleChannelId = 'sample-channel';

export const channelPath = `/api/v1/channels/${sampleChannelId}`;

// A moment a few minutes or hours before now, so a sample row reads as recent whenever the page is opened.
export function minutesAgo(minutes: number): string {
  return new Date(Date.now() - minutes * 60_000).toISOString();
}

type Moment = { $ago: number };

function isMoment(value: unknown): value is Moment {
  return typeof value === 'object' && value !== null && '$ago' in value && typeof value.$ago === 'number';
}

// A captured answer of a fresh install, ready to serve. The capture holds every time as minutes before the moment
// the install finished seeding (`{ "$ago": 12 }`); here that moment is the sample channel's install date, so a
// seeded row reads as created then and nothing in the page depends on the day it was captured.
export function fixture<T>(raw: T): T {
  if (isMoment(raw))
    return minutesAgo(sampleChannel.installedMinutesAgo + raw.$ago) as T;

  if (Array.isArray(raw))
    return raw.map((item) => fixture(item)) as T;

  if (typeof raw === 'object' && raw !== null)
    return fixtureObject(raw) as T;

  return raw;
}

function fixtureObject(raw: object): object {
  const entries = Object.entries(raw).map(([key, value]) => [key, fixture(value)]);

  return Object.fromEntries(entries);
}

// The bot wraps a single record as { data } and a list as { data, hasMore, total }.
export function one(data: object | null): { status: string; data: object | null } {
  return { status: 'success', data };
}

export function list(rows: readonly object[]): {
  status: string;
  data: readonly object[];
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
export function get(path: string, body: object | ((url: URL) => object)) {
  return {
    method: 'GET',
    path,
    body: typeof body === 'function'
      ? (url: URL): Json => body(url) as Json
      : (): Json => body as Json,
  } as const satisfies FakeReply;
}
