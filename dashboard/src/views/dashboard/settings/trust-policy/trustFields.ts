// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { TrustPolicy } from '@/api/trustPolicy';

// What the Trust policy page shows: the groups, the fields in each and the step of each number input. The
// words sit in i18n under dashboard.settings.trustPolicy. The bounds are the bot's; the page shows its refusal.
export type TrustFieldKey = keyof Omit<TrustPolicy, 'isPinned'>;
export type NumberFieldKey = Exclude<TrustFieldKey, 'reputationBoostEnabled'>;

export type TrustField =
  | { key: NumberFieldKey; step: number } |
  { key: 'reputationBoostEnabled'; toggle: true };

export type TrustGroup = {
  key: string;
  fields: readonly TrustField[];
};

export const trustGroups = [
  {
    key: 'weights',
    fields: [
      { key: 'requestCountWeight', step: 0.05 },
      { key: 'accountAgeWeight', step: 0.05 },
      { key: 'contentAgeWeight', step: 0.05 },
      { key: 'contentPopularityWeight', step: 0.05 },
    ],
  },
  {
    key: 'growth',
    fields: [
      { key: 'requestCountDecay', step: 0.1 },
      { key: 'accountAgeDecay', step: 0.1 },
      { key: 'contentAgeDecay', step: 0.1 },
      { key: 'contentPopularityDecay', step: 0.1 },
    ],
  },
  {
    key: 'boosts',
    fields: [
      { key: 'reputationBoostEnabled', toggle: true },
      { key: 'affiliateBoost', step: 1 },
      { key: 'partnerBoost', step: 1 },
      { key: 'verifiedYouTubeBoost', step: 1 },
      { key: 'notFollowingFactor', step: 0.05 },
      { key: 'youTubeQualityPenaltyFactor', step: 0.05 },
    ],
  },
  {
    key: 'penalties',
    fields: [
      { key: 'skipPenalty', step: 1 },
      { key: 'timeoutPenalty', step: 1 },
      { key: 'banPenalty', step: 1 },
    ],
  },
  {
    key: 'tiers',
    fields: [
      { key: 'untrustedMax', step: 1 },
      { key: 'lowMax', step: 1 },
      { key: 'standardMax', step: 1 },
    ],
  },
  {
    key: 'heat',
    fields: [
      { key: 'heatHalfLifeHours', step: 1 },
      { key: 'heatDeltaBan', step: 1 },
      { key: 'heatDeltaTimeout', step: 1 },
      { key: 'heatDeltaReportValidated', step: 1 },
      { key: 'heatDeltaAutoModDenied', step: 1 },
      { key: 'heatDeltaFilterHit', step: 1 },
    ],
  },
] as const satisfies readonly TrustGroup[];

export const trustFieldKeys: readonly TrustFieldKey[] = trustGroups.flatMap((group) =>
  group.fields.map((field) => field.key),
);

export const weightKeys = [
  'requestCountWeight',
  'accountAgeWeight',
  'contentAgeWeight',
  'contentPopularityWeight',
] as const satisfies readonly NumberFieldKey[];

// The page edits plain numbers. The bot may send a number as text, so every value is converted once, here.
export type TrustDraft = Record<NumberFieldKey, number> & { reputationBoostEnabled: boolean };

const numberKeys = trustFieldKeys.filter(
  (key): key is NumberFieldKey => key !== 'reputationBoostEnabled',
);

export function toDraft(policy: TrustPolicy) {
  const entries = numberKeys.map((key) => [key, Number(policy[key])]);

  return {
    ...(Object.fromEntries(entries) as Record<NumberFieldKey, number>),
    reputationBoostEnabled: policy.reputationBoostEnabled,
  } as const satisfies TrustDraft;
}

export function sameDraft(a: TrustDraft, b: TrustDraft): boolean {
  return trustFieldKeys.every((key) => a[key] === b[key]);
}

// Floating-point sums such as 0.1 + 0.2 show without noise: two decimals, trailing zeros trimmed.
export function weightSum(draft: TrustDraft): number {
  const sum = weightKeys.reduce((total, key) => total + draft[key], 0);

  return Math.round(sum * 100) / 100;
}
