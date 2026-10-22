// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { FakeReply } from '../fakeApi';
import { channelPath, get, list, minutesAgo, one } from './shape';

const dayMinutes = 60 * 24;
const history = 365;

// A calendar day `back` days before today, as YYYY-MM-DD.
function dayOf(back: number): string {
  return minutesAgo(back * dayMinutes).slice(0, 10);
}

// A steady wave with a weekend lift, so the chart reads as a real month and needs no random numbers.
function wave(back: number, base: number, swing: number): number {
  const weekend = back % 7 < 2
    ? 1.25
    : 1;

  return Math.round((base + swing * Math.sin(back / 2.3)) * weekend);
}

function dailyRow(back: number) {
  const streamed = back % 7 !== 3;

  return {
    activityDate: dayOf(back),
    uniqueChatters: streamed
      ? wave(back, 380, 90)
      : 0,
    totalMessages: streamed
      ? wave(back, 5200, 1100)
      : 0,
    totalWatchSeconds: streamed
      ? wave(back, 640_000, 120_000)
      : 0,
    newFollowers: streamed
      ? wave(back, 46, 18)
      : 3,
    newSubscribers: streamed
      ? wave(back, 14, 6)
      : 1,
    bitsCheered: streamed
      ? wave(back, 2400, 900)
      : 0,
    commandsRun: streamed
      ? wave(back, 610, 140)
      : 0,
    redemptionsCount: streamed
      ? wave(back, 88, 30)
      : 0,
    songRequests: streamed
      ? wave(back, 31, 12)
      : 0,
    currencyEarnedTotal: streamed
      ? wave(back, 41_000, 9000)
      : 0,
    currencySpentTotal: streamed
      ? wave(back, 22_000, 6000)
      : 0,
    gamesPlayed: streamed
      ? 2
      : 0,
    peakViewers: streamed
      ? wave(back, 1100, 260)
      : null,
  };
}

const days = Array.from({ length: history }, (_, back) =>
  dailyRow(back)).reverse();

const streamNames = [
  ['Ranked climb, day 9', 'Valorant'],
  ['Viewer games and raid train', 'Jackbox Party Pack'],
  ['Cozy farming night', 'Stardew Valley'],
  ['Boss rush with chat voting', 'Elden Ring'],
  ['Subathon hour 6', 'Just Chatting'],
] as const;

const streams = days
  .filter((day) => Number(day.totalMessages) > 0)
  .reverse()
  .map((day, index) => {
    const [title, game] = streamNames[index % streamNames.length] ?? streamNames[0];
    const seconds = 3 * 3600 + (index % 4) * 1800;
    const started = `${day.activityDate}T18:00:00.000Z`;

    return {
      streamId: `sample-stream-${index}`,
      title,
      gameName: game,
      startedAt: started,
      endedAt: new Date(Date.parse(started) + seconds * 1000).toISOString(),
      durationSeconds: seconds,
      peakViewers: day.peakViewers,
      totalMessages: day.totalMessages,
      uniqueChatters: day.uniqueChatters,
      newFollowers: day.newFollowers,
    };
  });

function inRange(url: URL): typeof days {
  const from = url.searchParams.get('from') ?? '';

  return days.filter((day) => day.activityDate >= from);
}

function sum(rows: typeof days, key: 'totalMessages' | 'newFollowers' | 'newSubscribers' | 'bitsCheered' | 'commandsRun' | 'redemptionsCount' | 'songRequests' | 'currencyEarnedTotal' | 'currencySpentTotal'): number {
  return rows.reduce((total, row) => total + Number(row[key]), 0);
}

function summary(url: URL) {
  const rows = inRange(url);
  const peaks = rows.map((row) => Number(row.peakViewers ?? 0));

  return {
    from: url.searchParams.get('from') ?? dayOf(29),
    to: url.searchParams.get('to') ?? dayOf(0),
    totalMessages: sum(rows, 'totalMessages'),
    newFollowers: sum(rows, 'newFollowers'),
    newSubscribers: sum(rows, 'newSubscribers'),
    bitsCheered: sum(rows, 'bitsCheered'),
    commandsRun: sum(rows, 'commandsRun'),
    redemptionsCount: sum(rows, 'redemptionsCount'),
    songRequests: sum(rows, 'songRequests'),
    currencyEarnedTotal: sum(rows, 'currencyEarnedTotal'),
    currencySpentTotal: sum(rows, 'currencySpentTotal'),
    peakViewers: Math.max(...peaks),
    deltas: {
      messagesPct: 12.4,
      followersPct: 8.1,
      subscribersPct: 21.5,
      bitsPct: -4.2,
      commandsPct: 6.7,
      redemptionsPct: 15.3,
    },
  };
}

// Every weekday and hour gets a count, heaviest in the evening, so the heat grid has a clear shape.
const chatHours = {
  timeZone: 'UTC',
  cells: Array.from({ length: 7 * 24 }, (_, index) => {
    const hour = index % 24;
    const dayOfWeek = Math.floor(index / 24);
    const evening = Math.max(0, 1 - Math.abs(hour - 20) / 6);
    // Each weekday has its own pull, and the weekend is the heaviest.
    const pull = [0.8, 0.95, 0.7, 1.1, 1.3, 1.6, 1.4][dayOfWeek] ?? 1;

    return {
      dayOfWeek,
      hour,
      messages: Math.round(evening * 620 * pull),
      chatters: Math.round(evening * 130 * pull),
    };
  }),
};

export const analyticsReplies: FakeReply[] = [
  get(`${channelPath}/analytics/channel/summary`, (url) =>
    one(summary(url))),
  get(`${channelPath}/analytics/channel/daily`, (url) =>
    one(inRange(url))),
  get(`${channelPath}/analytics/channel/chat-hours`, one(chatHours)),
  get(`${channelPath}/analytics/streams`, list(streams)),
];
