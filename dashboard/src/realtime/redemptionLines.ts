// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { ChatLine, ChatLineRedemption } from '@/api/chat';

import type { RedemptionActivityPayload, RewardRedeemedPayload } from './payloads';

// What every redemption line has in common: a chat line from a viewer who sent no message of their own.
function redemptionLine(
  id: string,
  who: Pick<ChatLine, 'userId' | 'displayName' | 'username' | 'channelId' | 'timestamp' | 'message'>,
  redemption: ChatLineRedemption['redemption'],
): ChatLine {
  return {
    ...who,
    id: `redemption:${id}`,
    fragments: [],
    userType: 'viewer',
    isSubscriber: false,
    isVip: false,
    isArtist: false,
    isModerator: false,
    isBroadcaster: false,
    isCheer: false,
    isCommand: false,
    badges: [],
    bitsAmount: 0,
    color: null,
    messageType: 'redemption',
    provider: 'twitch',
    redemption,
  };
}

// A custom channel-point reward. The reward event names no login, so the display name stands in for it.
export function rewardLine(reward: RewardRedeemedPayload): ChatLine {
  return redemptionLine(
    reward.redemptionId,
    {
      userId: reward.userId,
      displayName: reward.userDisplayName,
      username: reward.userDisplayName,
      channelId: reward.broadcasterId,
      timestamp: reward.timestamp,
      message: reward.userInput ?? '',
    },
    {
      kind: 'reward',
      title: reward.rewardTitle,
      cost: reward.cost,
      costUnit: 'channel_points',
    },
  );
}

// An automatic reward or a Power-up.
export function activityLine(activity: RedemptionActivityPayload): ChatLine {
  return redemptionLine(
    activity.redemptionId,
    {
      userId: activity.userId,
      displayName: activity.userDisplayName,
      username: activity.userLogin,
      channelId: activity.broadcasterId,
      timestamp: activity.timestamp,
      message: activity.userInput ?? '',
    },
    {
      kind: activity.kind,
      title: activity.title,
      cost: activity.cost,
      costUnit: activity.costUnit,
    },
  );
}
