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

import type { Poll, PollHistoryItem, PollHistoryOption, Prediction } from '@/api/liveOps';
import type { StatusTone } from '@/components/layout/StatusBadge.vue';

export const historyKinds = ['all', 'poll', 'prediction', 'chat-poll'] as const;

export type HistoryKind = (typeof historyKinds)[number];

// The rows the kind filter keeps; 'all' keeps every row.
export function filterByKind(
  items: readonly PollHistoryItem[],
  kind: HistoryKind,
): PollHistoryItem[] {
  if (kind === 'all')
    return [...items];

  return items.filter((item) => item.kind === kind);
}

const percentScale = 100;
const meaningfulStatuses = ['canceled', 'terminated', 'unfinished'];

// One choice or outcome of the running poll or prediction, ready to draw as a bar.
export type LeadBar = {
  id: string;
  title: string;
  votes: number;
  points: number;
  percent: number;
  leading: boolean;
};

export type VoteSplit = {
  voted: PollHistoryItem[];
  empty: PollHistoryItem[];
};

// Polls nobody voted on fold into one line; the rest stay as cards.
export function splitByVotes(items: readonly PollHistoryItem[]) {
  return {
    voted: items.filter((item) => Number(item.totalVotes) > 0),
    empty: items.filter((item) => Number(item.totalVotes) <= 0),
  } as const satisfies VoteSplit;
}

// Only a status that changes the reading of the result earns a label: refunded, canceled or ended early.
export function showsStatus(status: string): boolean {
  return meaningfulStatuses.includes(status.toLowerCase());
}

// The option marked winner, or null when the poll has none.
export function winnerOf(item: PollHistoryItem): PollHistoryOption | null {
  return item.options.find((option) => option.isWinner) ?? null;
}

function toBars(
  rows: readonly {
    id: string;
    title: string;
    votes: number;
    points: number;
  }[],
): LeadBar[] {
  const total = rows.reduce((sum, row) => sum + row.votes, 0);
  const top = Math.max(0, ...rows.map((row) => row.votes));

  return rows.map((row) => ({
    ...row,
    percent: total > 0
      ? Math.round((row.votes / total) * percentScale)
      : 0,
    leading: top > 0 && row.votes === top,
  }));
}

export function pollBars(poll: Poll): LeadBar[] {
  return toBars(
    poll.choices.map((choice) => ({
      id: choice.id,
      title: choice.title,
      votes: Number(choice.votes),
      points: Number(choice.channelPointsVotes),
    })),
  );
}

// A prediction splits by the points wagered; the viewers who chose it show as its votes.
export function predictionBars(prediction: Prediction): LeadBar[] {
  const wagers = prediction.outcomes.map((outcome) => Number(outcome.channelPoints));
  const total = wagers.reduce((sum, points) => sum + points, 0);
  const bars = toBars(
    prediction.outcomes.map((outcome) => ({
      id: outcome.id,
      title: outcome.title,
      votes: Number(outcome.users),
      points: Number(outcome.channelPoints),
    })),
  );

  return bars.map((bar) => ({
    ...bar,
    percent: total > 0
      ? Math.round((bar.points / total) * percentScale)
      : 0,
    leading: total > 0 && bar.points === Math.max(...bars.map((other) => other.points)),
  }));
}

// Still taking votes reads live, a normal end reads ok, a canceled or lost one reads off.
export function statusTone(status: string): StatusTone {
  const normalized = status.toLowerCase();

  if (normalized === 'active' || normalized === 'open' || normalized === 'locked')
    return 'live';

  if (normalized === 'completed' || normalized === 'resolved' || normalized === 'closed')
    return 'ok';

  return 'off';
}
