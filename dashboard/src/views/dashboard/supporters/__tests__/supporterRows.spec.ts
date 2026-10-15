// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { describe, expect, it } from 'vitest';

import type { ActivityEvent } from '@/api/home';

import { cheerRows, subscriptionRows, topCheerers, topGifters } from '../supporterRows';

function event(id: string, type: string, username: string | null, data: object): ActivityEvent {
  return {
    id,
    type,
    userId: username === null
      ? null
      : `u-${username}`,
    username,
    data: JSON.stringify(data),
    timestamp: '2026-10-15T12:00:00Z',
  };
}

describe('subscriptionRows', () => {
  it('marks a gifted sub and keeps other events out', () => {
    const rows = subscriptionRows([
      event('1', 'channel.subscribe', 'ann', { tier: '1000', isGift: true }),
      event('2', 'channel.follow', 'bob', {}),
    ]);

    expect(rows).toHaveLength(1);

    expect(rows[0]).toMatchObject({
      kind: 'subscribe',
      tier: '1',
      receivedAsGift: true,
    });
  });

  it('shows an anonymous gifter without a name or profile link', () => {
    const rows = subscriptionRows([
      event('3', 'channel.subscription.gift', 'ghost', {
        tier: '2000',
        giftCount: 5,
        isAnonymous: true,
      }),
    ]);

    expect(rows[0]?.person).toEqual({
      userId: null,
      name: null,
      anonymous: true,
    });

    expect(rows[0]).toMatchObject({
      kind: 'gift',
      giftCount: 5,
      tier: '2',
    });
  });
});

describe('cheers and top supporters', () => {
  const cheers = cheerRows([
    event('a', 'channel.cheer', 'ann', { bits: 100 }),
    event('b', 'channel.cheer', 'ann', { bits: 50 }),
    event('c', 'channel.cheer', 'bob', { bits: 120 }),
    event('d', 'channel.cheer', 'x', { bits: 999, isAnonymous: true }),
  ]);

  it('sums bits per named person, highest first, and skips anonymous cheers', () => {
    const top = topCheerers(cheers);

    expect(top.map((entry) => [entry.person.name, entry.total])).toEqual([
      ['ann', 150],
      ['bob', 120],
    ]);
  });

  it('counts gifted subs per gifter', () => {
    const events = [
      event('1', 'channel.subscription.gift', 'ann', { tier: '1000', giftCount: 3 }),
      event('2', 'channel.subscription.gift', 'ann', { tier: '1000', giftCount: 2 }),
    ];
    const rows = subscriptionRows(events);
    const top = topGifters(rows);

    expect(top).toHaveLength(1);
    expect(top[0]?.total).toBe(5);
  });
});
