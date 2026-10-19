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
      : `guid-${username}`,
    twitchUserId: username === null
      ? null
      : `u-${username}`,
    color: '#FF4500',
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

  it('names a supporter by Twitch user id and chat color, so the name opens their profile', () => {
    const rows = subscriptionRows([event('5', 'channel.subscribe', 'ann', { tier: '1000' })]);

    expect(rows[0]?.person).toEqual({
      userId: 'u-ann',
      name: 'ann',
      color: '#FF4500',
      anonymous: false,
    });
  });

  it('names the gifter on a gifted sub from the received-gift event of the same recipient', () => {
    const rows = subscriptionRows([
      event('1', 'channel.subscribe', 'ann', { tier: '1000', isGift: true }),
      event('2', 'channel.subscription.gift.received', null, {
        recipientUserId: 'u-ann',
        gifterUserId: 'u-bob',
        gifterDisplayName: 'bob',
        isAnonymous: false,
      }),
    ]);

    expect(rows).toHaveLength(1);

    expect(rows[0]?.gifter).toEqual({
      userId: 'u-bob',
      name: 'bob',
      color: null,
      anonymous: false,
    });
  });

  it('shows an anonymous gifter of a received sub as anonymous without a profile', () => {
    const rows = subscriptionRows([
      event('1', 'channel.subscribe', 'ann', { tier: '1000', isGift: true }),
      event('2', 'channel.subscription.gift.received', null, {
        recipientUserId: 'u-ann',
        gifterUserId: '',
        gifterDisplayName: '',
        isAnonymous: true,
      }),
    ]);

    expect(rows[0]?.gifter).toEqual({
      userId: null,
      name: null,
      color: null,
      anonymous: true,
    });
  });

  it('lists a received gift that has no subscribe row as its own row naming recipient and gifter', () => {
    const rows = subscriptionRows([
      {
        ...event('2', 'channel.subscription.gift.received', 'zed', {
          recipientUserId: 'u-zed',
          recipientDisplayName: 'Zed',
          gifterUserId: 'u-bob',
          gifterDisplayName: 'bob',
          tier: '1000',
          isAnonymous: false,
        }),
      },
    ]);

    expect(rows).toHaveLength(1);

    expect(rows[0]).toMatchObject({
      kind: 'subscribe',
      receivedAsGift: true,
      tier: '1',
      person: {
        userId: 'u-zed',
        name: 'Zed',
        anonymous: false,
      },
      gifter: {
        userId: 'u-bob',
        name: 'bob',
        anonymous: false,
      },
    });
  });

  it('does not list a received gift twice when its subscribe row exists', () => {
    const rows = subscriptionRows([
      event('1', 'channel.subscribe', 'ann', { tier: '1000', isGift: true }),
      event('2', 'channel.subscription.gift.received', 'ann', {
        recipientUserId: 'u-ann',
        recipientDisplayName: 'ann',
        gifterUserId: 'u-bob',
        gifterDisplayName: 'bob',
      }),
    ]);

    expect(rows).toHaveLength(1);
    expect(rows[0]?.gifter?.name).toBe('bob');
  });

  it('leaves the gifter empty when no received-gift event matches the recipient', () => {
    const rows = subscriptionRows([
      event('1', 'channel.subscribe', 'ann', { tier: '1000', isGift: true }),
      event('2', 'channel.subscription.gift.received', null, {
        recipientUserId: 'u-zed',
        gifterUserId: 'u-bob',
        gifterDisplayName: 'bob',
        isAnonymous: false,
      }),
    ]);

    expect(rows[0]?.gifter).toBeNull();
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
      color: null,
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
