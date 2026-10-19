// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { ActivityEvent } from '@/api/home';
import { lookup } from '@/lib/array';
import {
  parseActivityData,
  pickCount,
  pickText,
  tierOf,
  type ActivityData,
} from '@/views/dashboard/home/components/activityData';

// One person on a row. A null userId means no profile to open: an anonymous cheer or gift.
export type Person = {
  userId: string | null;
  name: string | null;
  color: string | null;
  anonymous: boolean;
};

export type SubscriptionRow = {
  id: string;
  person: Person;
  kind: 'subscribe' | 'resubscribe' | 'gift';
  tier: string | null;
  months: number | null;
  // A gift row carries how many subs went out; a subscribe row marked gifted came from somebody else.
  giftCount: number | null;
  receivedAsGift: boolean;
  // Who gifted this sub, from the received-gift event of the same recipient. Null when none is journaled.
  gifter: Person | null;
  timestamp: string;
};

export type CheerRow = {
  id: string;
  person: Person;
  bits: number;
  timestamp: string;
};

export type TopEntry = {
  key: string;
  person: Person;
  total: number;
};

const subscriptionKinds = {
  'channel.subscribe': 'subscribe',
  'channel.subscription.message': 'resubscribe',
  'channel.subscription.gift': 'gift',
} as const satisfies Record<string, SubscriptionRow['kind']>;

const topLimit = 5;

function personOf(event: ActivityEvent, data: ActivityData): Person {
  const anonymous = data.isAnonymous === true;
  const name = pickText(data, 'userDisplayName', 'gifterDisplayName') ?? event.username ?? null;

  return anonymous
    ? {
        userId: null,
        name: null,
        color: null,
        anonymous: true,
      }
    : {
        userId: event.twitchUserId ?? null,
        name,
        color: event.color ?? null,
        anonymous: false,
      };
}

function subscriptionRow(event: ActivityEvent, kind: SubscriptionRow['kind']) {
  const data = parseActivityData(event.data);

  return {
    id: event.id,
    person: personOf(event, data),
    kind,
    tier: tierOf(data.tier),
    months: pickCount(data, 'cumulativeMonths', 'months'),
    giftCount: pickCount(data, 'giftCount', 'count'),
    receivedAsGift: data.isGift === true,
    gifter: null,
    timestamp: event.timestamp,
  } as const satisfies SubscriptionRow;
}

const receivedGiftType = 'channel.subscription.gift.received';

function gifterOf(data: ActivityData): Person {
  const name = pickText(data, 'gifterDisplayName');

  return data.isAnonymous === true || name === null
    ? {
        userId: null,
        name: null,
        color: null,
        anonymous: true,
      }
    : {
        userId: pickText(data, 'gifterUserId'),
        name,
        color: null,
        anonymous: false,
      };
}

function gapMs(a: string, b: string): number {
  return Math.abs(Date.parse(a) - Date.parse(b));
}

// The received-gift event of the same recipient that sits nearest in time to the gifted subscribe row.
function nearestReceived(
  row: SubscriptionRow,
  recipientId: string | null,
  received: readonly ActivityEvent[],
): ActivityEvent | null {
  let best: ActivityEvent | null = null;

  for (const candidate of received) {
    const data = parseActivityData(candidate.data);

    if (recipientId === null || pickText(data, 'recipientUserId') !== recipientId) continue;

    if (
      best === null ||
      gapMs(candidate.timestamp, row.timestamp) < gapMs(best.timestamp, row.timestamp)
    ) {
      best = candidate;
    }
  }

  return best;
}

// A received gift with no subscribe row of its own: the recipient is the actor, the gifter rides in the data.
function receivedGiftRow(event: ActivityEvent): SubscriptionRow {
  const data = parseActivityData(event.data);
  const name = pickText(data, 'recipientDisplayName') ?? event.username ?? null;

  return {
    id: event.id,
    person: {
      userId: pickText(data, 'recipientUserId') ?? event.twitchUserId ?? null,
      name,
      color: event.color ?? null,
      anonymous: false,
    },
    kind: 'subscribe',
    tier: tierOf(data.tier),
    months: null,
    giftCount: null,
    receivedAsGift: true,
    gifter: gifterOf(data),
    timestamp: event.timestamp,
  };
}

// Twitch sends a subscribe and a resub message for one resub the viewer shares a message with.
// Both rows are the same action when they come from one person within this window.
const sameActionMs = 60_000;

function isSameAction(subscribe: SubscriptionRow, resub: SubscriptionRow): boolean {
  return (
    subscribe.person.userId !== null &&
    subscribe.person.userId === resub.person.userId &&
    gapMs(subscribe.timestamp, resub.timestamp) <= sameActionMs
  );
}

function foldResubs(rows: readonly SubscriptionRow[]): SubscriptionRow[] {
  const resubs = rows.filter((row) => row.kind === 'resubscribe');

  return rows.filter(
    (row) =>
      row.kind !== 'subscribe' ||
      row.receivedAsGift ||
      !resubs.some((resub) => isSameAction(row, resub)),
  );
}

export function subscriptionRows(events: readonly ActivityEvent[]): SubscriptionRow[] {
  const rows: SubscriptionRow[] = [];
  const received = events.filter((event) => event.type === receivedGiftType);
  const matched = new Set<string>();

  for (const event of events) {
    const kind = lookup(subscriptionKinds, event.type);

    if (kind === undefined) continue;

    const row = subscriptionRow(event, kind);
    const match = row.receivedAsGift
      ? nearestReceived(row, event.twitchUserId ?? null, received)
      : null;

    if (match !== null) {
      matched.add(match.id);
    }

    rows.push(
      match === null
        ? row
        : {
            ...row,
            gifter: gifterOf(parseActivityData(match.data)),
          },
    );
  }

  for (const event of received) {
    if (!matched.has(event.id)) {
      rows.push(receivedGiftRow(event));
    }
  }

  return foldResubs(rows);
}

export function cheerRows(events: readonly ActivityEvent[]): CheerRow[] {
  const rows: CheerRow[] = [];

  for (const event of events) {
    if (event.type !== 'channel.cheer') continue;

    const data = parseActivityData(event.data);
    const bits = pickCount(data, 'bits');

    if (bits !== null) {
      rows.push({
        id: event.id,
        person: personOf(event, data),
        bits,
        timestamp: event.timestamp,
      });
    }
  }

  return rows;
}

// Totals per named person; an anonymous giver cannot be ranked.
function ranked(entries: readonly { person: Person; amount: number }[]): TopEntry[] {
  const totals = new Map<string, TopEntry>();

  for (const entry of entries) {
    if (entry.person.anonymous || entry.person.name === null) continue;

    const key = entry.person.userId ?? entry.person.name;
    const known = totals.get(key);

    if (known === undefined) {
      totals.set(key, {
        key,
        person: entry.person,
        total: entry.amount,
      });
    } else {
      known.total += entry.amount;
    }
  }

  return [...totals.values()].sort((a, b) => b.total - a.total).slice(0, topLimit);
}

export function topCheerers(rows: readonly CheerRow[]): TopEntry[] {
  return ranked(rows.map((row) => ({ person: row.person, amount: row.bits })));
}

export function topGifters(rows: readonly SubscriptionRow[]): TopEntry[] {
  const gifts = rows.filter((row) => row.kind === 'gift');

  return ranked(gifts.map((row) => ({ person: row.person, amount: row.giftCount ?? 1 })));
}
