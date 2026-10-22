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
import { channelPath, get, one } from './shape';

type Knob = {
  key: string;
  group: string;
  toggle?: boolean;
  minimum?: number;
  maximum?: number;
};

// One entry per knob the spam-defense page groups and bounds. The copy of each knob comes from the dashboard's
// own translations; the bot only sends the key and the limits.
const knobs: Knob[] = [
  {
    key: 'IsEnabled',
    group: 'master',
    toggle: true,
  },
  {
    key: 'DryRun',
    group: 'master',
    toggle: true,
  },
  {
    key: 'SemiTrustedWatchHoursHere',
    group: 'trust',
    minimum: 0,
    maximum: 500,
  },
  {
    key: 'SemiTrustedWatchHoursInstance',
    group: 'trust',
    minimum: 0,
    maximum: 1000,
  },
  {
    key: 'AccountAgeGateDays',
    group: 'newcomers',
    minimum: 0,
    maximum: 365,
  },
  {
    key: 'FollowAgeGateDays',
    group: 'newcomers',
    minimum: 0,
    maximum: 365,
  },
  {
    key: 'AccountGateHoldsForReview',
    group: 'newcomers',
    toggle: true,
  },
  {
    key: 'NearDuplicateSimilarity',
    group: 'content',
    minimum: 0.5,
    maximum: 1,
  },
  {
    key: 'MinimumSkeletonLength',
    group: 'content',
    minimum: 3,
    maximum: 50,
  },
  {
    key: 'NonLatinScriptGate',
    group: 'content',
    toggle: true,
  },
  {
    key: 'QualifyNoStandingShare',
    group: 'campaign',
    minimum: 0.1,
    maximum: 1,
  },
  {
    key: 'DequalifyNoStandingShare',
    group: 'campaign',
    minimum: 0.05,
    maximum: 1,
  },
  {
    key: 'MinimumCohortSize',
    group: 'campaign',
    minimum: 2,
    maximum: 50,
  },
  {
    key: 'WindowSeconds',
    group: 'bursts',
    minimum: 10,
    maximum: 600,
  },
  {
    key: 'MaxWindowSeconds',
    group: 'bursts',
    minimum: 10,
    maximum: 1800,
  },
  {
    key: 'ActionDelaySeconds',
    group: 'bursts',
    minimum: 0,
    maximum: 30,
  },
  {
    key: 'AutoReverseOnDequalify',
    group: 'bursts',
    toggle: true,
  },
  {
    key: 'FollowSpikeFactor',
    group: 'lockdown',
    minimum: 1,
    maximum: 50,
  },
  {
    key: 'JoinBurstFactor',
    group: 'lockdown',
    minimum: 1,
    maximum: 50,
  },
  {
    key: 'LockdownMinutes',
    group: 'lockdown',
    minimum: 1,
    maximum: 120,
  },
  {
    key: 'LockdownAutoExtend',
    group: 'lockdown',
    toggle: true,
  },
  {
    key: 'LockdownMaxMinutes',
    group: 'lockdown',
    minimum: 1,
    maximum: 240,
  },
  {
    key: 'NetworkSubscribe',
    group: 'network',
    toggle: true,
  },
  {
    key: 'NetworkContribute',
    group: 'network',
    toggle: true,
  },
  {
    key: 'RequiredCorroborations',
    group: 'network',
    minimum: 1,
    maximum: 10,
  },
];

const descriptors = knobs.map((knob) => ({
  key: knob.key,
  group: knob.group,
  labelKey: `spam_setting_${knob.key}_label`,
  explanationKey: `spam_setting_${knob.key}_explanation`,
  costKey: `spam_setting_${knob.key}_cost`,
  isToggle: knob.toggle ?? false,
  minimum: knob.minimum ?? null,
  maximum: knob.maximum ?? null,
  pendingSlice: null,
  inactiveReasonKey: null,
}));

const policy = {
  settings: {
    isEnabled: true,
    dryRun: false,
    trustThresholds: {
      establishedDays: 30,
      establishedMessages: 100,
      establishedDistinctActiveDays: 10,
      establishedStrikeFreeDays: 14,
    },
    semiTrustedWatchHoursHere: 5,
    semiTrustedWatchHoursInstance: 20,
    accountAgeGateDays: 7,
    followAgeGateDays: 2,
    accountGateHoldsForReview: true,
    nearDuplicateSimilarity: 0.85,
    minimumSkeletonLength: 8,
    nonLatinScriptGate: false,
    qualifyNoStandingShare: 0.6,
    dequalifyNoStandingShare: 0.3,
    minimumCohortSize: 5,
    windowSeconds: 60,
    maxWindowSeconds: 300,
    actionDelaySeconds: 2,
    autoReverseOnDequalify: true,
    followSpikeFactor: 8,
    joinBurstFactor: 6,
    lockdownMinutes: 10,
    lockdownAutoExtend: true,
    lockdownMaxMinutes: 60,
    networkSubscribe: true,
    networkContribute: false,
    requiredCorroborations: 3,
  },
  // cspell:disable-next-line -- the bot's wire name for the list
  catalogue: descriptors,
  invariants: ['sd0', 'sd8', 'sd9', 'sd11', 'sd12'].map((decision) => ({
    decision: decision.toUpperCase(),
    guaranteeKey: `spam_invariant_${decision}`,
  })),
  isPinned: false,
};

export const moderationReplies: FakeReply[] = [get(`${channelPath}/spam-defense/policy`, one(policy))];
