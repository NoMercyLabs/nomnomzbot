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
  const name = pickText(data, 'userDisplayName', 'gifterDisplayName') ??
    event.username ??
    null;

  return anonymous
    ? {
        userId: null,
        name: null,
        anonymous: true,
      }
    : {
        userId: event.userId ?? null,
        name,
        anonymous: false,
      };
}

function subscriptionRow(event: ActivityEvent, kind: SubscriptionRow['kind']): SubscriptionRow {
  const data = parseActivityData(event.data);

  return {
    id: event.id,
    person: personOf(event, data),
    kind,
    tier: tierOf(data.tier),
    months: pickCount(data, 'cumulativeMonths', 'months'),
    giftCount: pickCount(data, 'giftCount', 'count'),
    receivedAsGift: data.isGift === true,
    timestamp: event.timestamp,
  };
}

export function subscriptionRows(events: readonly ActivityEvent[]): SubscriptionRow[] {
  const rows: SubscriptionRow[] = [];

  for (const event of events) {
    const kind = subscriptionKinds[event.type as keyof typeof subscriptionKinds];

    if (kind !== undefined) {
      rows.push(subscriptionRow(event, kind));
    }
  }

  return rows;
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

  return [...totals.values()]
    .sort((a, b) => b.total - a.total)
    .slice(0, topLimit);
}

export function topCheerers(rows: readonly CheerRow[]): TopEntry[] {
  return ranked(rows.map((row) => ({ person: row.person, amount: row.bits })));
}

export function topGifters(rows: readonly SubscriptionRow[]): TopEntry[] {
  const gifts = rows.filter((row) => row.kind === 'gift');

  return ranked(gifts.map((row) => ({ person: row.person, amount: row.giftCount ?? 1 })));
}
