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
  it('folds the subscribe and the resub message of one action into one resub row', () => {
    const rows = subscriptionRows([
      event('1', 'channel.subscribe', 'kani', { tier: '1000' }),
      event('2', 'channel.subscription.message', 'kani', { tier: '1000', cumulativeMonths: 3 }),
      event('3', 'channel.subscribe', 'other', { tier: '1000' }),
    ]);

    expect(rows.map((row) => row.id)).toEqual(['2', '3']);

    expect(rows[0]).toMatchObject({
      kind: 'resubscribe',
      months: 3,
    });
  });

  it('keeps a subscribe and a resub of the same person that are far apart in time', () => {
    const later = {
      ...event('2', 'channel.subscription.message', 'kani', { tier: '1000', cumulativeMonths: 4 }),
      timestamp: '2026-11-15T12:00:00Z',
    };

    const rows = subscriptionRows([
      event('1', 'channel.subscribe', 'kani', { tier: '1000' }),
      later,
    ]);

    expect(rows).toHaveLength(2);
  });

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

  it('reads the gifter and recipient from the dotted keys the live journal stores', () => {
    const rows = subscriptionRows([
      event('1', 'channel.subscription.gift.received', 'Poggers_Bear', {
        tier: '1000',
        user: 'Poggers_Bear',
        'user.id': 'u-pog',
        gifter: 'finwo_',
        'gifter.id': 'u-fin',
        anonymous: 'false',
      }),
    ]);

    expect(rows).toHaveLength(1);
    expect(rows[0]?.person.userId).toBe('u-pog');
    expect(rows[0]?.person.name).toBe('Poggers_Bear');

    expect(rows[0]?.gifter).toEqual({
      userId: 'u-fin',
      name: 'finwo_',
      color: null,
      anonymous: false,
    });
  });

  it('pairs a gifted subscribe row with the dotted received-gift event of its recipient', () => {
    const rows = subscriptionRows([
      event('1', 'channel.subscribe', 'ann', { tier: '1000', isGift: true }),
      event('2', 'channel.subscription.gift.received', null, {
        user: 'ann',
        'user.id': 'u-ann',
        gifter: 'bob',
        'gifter.id': 'u-bob',
        anonymous: 'false',
      }),
    ]);

    expect(rows).toHaveLength(1);
    expect(rows[0]?.gifter?.name).toBe('bob');
  });

  it('treats the string anonymous flag of the live journal as an anonymous gifter', () => {
    const rows = subscriptionRows([
      event('1', 'channel.subscription.gift.received', 'ann', {
        user: 'ann',
        'user.id': 'u-ann',
        gifter: 'ignored',
        anonymous: 'true',
      }),
    ]);

    expect(rows[0]?.gifter?.anonymous).toBe(true);
    expect(rows[0]?.gifter?.name).toBeNull();
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
