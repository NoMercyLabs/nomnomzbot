// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { costDrivingClass, type ResourceUsage, type UsageMetric } from '@/api/resourceLimits';

import { unitOf, type LimitUnit } from './limitUnits';

// How full a limited resource is. `unlimited` is a limit of -1: nothing stops the channel.
export type LimitLevel = 'ok' | 'near' | 'full' | 'unlimited';

export type LimitRow = {
  key: string;
  label: string;
  unit: LimitUnit;
  used: number;
  limit: number;
  percent: number;
  level: LimitLevel;
  // The flat abuse floor of a near-free resource; 0 for a cost-driving one.
  baseline: number;
};

export type MetricRow = {
  key: string;
  unit: LimitUnit;
  used: number;
  limit: number;
  remaining: number;
  percent: number;
  level: LimitLevel;
};

export type LimitGroups = {
  storage: LimitRow[];
  allowance: LimitRow[];
  perMessage: LimitRow[];
  nearFree: LimitRow[];
};

// A cap on one message, not a total used up over time: it has no usage to count, so it gets no bar.
const perMessageKeys = new Set<string>(['tts_max_characters']);

const nearAt = 80;

// The bot sends 64-bit numbers as a number or, when it is large, as a string.
export function toNumber(value: number | string): number {
  return Number(value);
}

function levelOf(used: number, limit: number): LimitLevel {
  if (limit < 0)
    return 'unlimited';

  if (used >= limit)
    return 'full';

  return percentOf(used, limit) >= nearAt
    ? 'near'
    : 'ok';
}

function percentOf(used: number, limit: number): number {
  if (limit <= 0)
    return 0;

  return Math.min(100, Math.round((used / limit) * 100));
}

function limitRowOf(item: ResourceUsage) {
  const used = toNumber(item.currentCount);
  const limit = toNumber(item.limit);

  return {
    key: item.limitKey,
    label: item.displayName,
    unit: unitOf(item.limitKey),
    used,
    limit,
    percent: percentOf(used, limit),
    level: levelOf(used, limit),
    baseline: toNumber(item.safetyBaseline),
  } as const satisfies LimitRow;
}

export function groupLimits(items: readonly ResourceUsage[]) {
  const costDriving = items
    .filter((item) => item.class === costDrivingClass)
    .map(limitRowOf);
  const nearFree = items
    .filter((item) => item.class !== costDrivingClass)
    .map(limitRowOf);

  return {
    storage: costDriving.filter((row) => row.unit === 'bytes'),
    allowance: costDriving.filter((row) => row.unit !== 'bytes' && !perMessageKeys.has(row.key)),
    perMessage: costDriving.filter((row) => perMessageKeys.has(row.key)),
    nearFree,
  } as const satisfies LimitGroups;
}

export function metricRowOf(item: UsageMetric) {
  const used = toNumber(item.used);
  const limit = toNumber(item.limit);

  return {
    key: item.metricKey,
    unit: unitOf(item.metricKey),
    used,
    limit,
    remaining: toNumber(item.remaining),
    percent: percentOf(used, limit),
    level: levelOf(used, limit),
  } as const satisfies MetricRow;
}
