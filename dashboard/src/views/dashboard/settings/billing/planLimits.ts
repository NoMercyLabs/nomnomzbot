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

import type { BillingTier } from '@/api/billing';

import { formatAmount, unitOf } from '../resource-limits/limitUnits';

// The limits a plan sets, in the order a person compares them. The server still sends tier rows for keys
// that are not plan limits (custom commands, timers, event responses, response variations); those are left
// out here and reported as a server gap.
export const planLimitKeys = [
  'sound_clip_storage_bytes',
  'channel_asset_storage_bytes',
  'sandbox_exec_ms',
  'tts_max_characters',
] as const satisfies readonly string[];

export type PlanLimitKey = (typeof planLimitKeys)[number];

export type PlanLimitCell =
  | { kind: 'missing' } |
  { kind: 'unlimited' } |
  { kind: 'characters'; count: number } |
  { kind: 'amount'; text: string };

const characterKeys = new Set<string>(['tts_max_characters']);

// A limit of -1 is "no limit". A plan with no row for a limit does not include it.
export function planLimitCell(tier: BillingTier, key: PlanLimitKey, locale: string) {
  const found = tier.limits.find((limit) => limit.limitKey === key);

  if (found === undefined)
    return { kind: 'missing' } as const satisfies PlanLimitCell;

  const value = Number(found.limitValue);

  if (value < 0)
    return { kind: 'unlimited' } as const satisfies PlanLimitCell;

  if (characterKeys.has(key))
    return { kind: 'characters', count: value } as const satisfies PlanLimitCell;

  return { kind: 'amount', text: formatAmount(value, unitOf(key), locale) } as const satisfies PlanLimitCell;
}

// Only the limits that at least one plan sets get a row.
export function shownLimitKeys(tiers: readonly BillingTier[]): PlanLimitKey[] {
  return planLimitKeys.filter((key) =>
    tiers.some((tier) => tier.limits.some((limit) => limit.limitKey === key)));
}
