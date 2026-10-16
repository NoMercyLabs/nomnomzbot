// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { redemptionsKey, type Redemption } from '@/api/rewards';

import type { HubEventHandler } from './hubEvent';
import type { RedemptionStatusChangedPayload, RewardRedeemedPayload } from './payloads';

// The pending-rewards panel follows the hub without a refetch: a new redemption joins the cached queue at the
// top, and a status change rewrites its row. Nothing is written when the queue was never loaded.
export const redemptionRedeemed = {
  event: 'RewardRedeemed',
  handle: ({ queries, channelId }, payload) => {
    queries.setQueryData<Redemption[]>(redemptionsKey(channelId), (rows) => {
      if (!rows || rows.some((row: Redemption) =>
        row.redemptionId === payload.redemptionId))
        return rows;

      const added: Redemption = {
        redemptionId: payload.redemptionId,
        rewardId: payload.rewardId,
        rewardTitle: payload.rewardTitle,
        userId: payload.userId,
        userDisplayName: payload.userDisplayName,
        userAvatarUrl: payload.avatarUrl,
        cost: payload.cost,
        userInput: payload.userInput,
        status: 'unfulfilled',
        redeemedAt: payload.timestamp,
      };

      return [added, ...rows];
    });
  },
} as const satisfies HubEventHandler<RewardRedeemedPayload>;

export const redemptionStatusChanged = {
  event: 'RedemptionStatusChanged',
  handle: ({ queries, channelId }, payload) => {
    queries.setQueryData<Redemption[]>(redemptionsKey(channelId), (rows) =>
      rows?.map((row: Redemption): Redemption =>
        row.redemptionId === payload.redemptionId
          ? { ...row, status: payload.status.toLowerCase() }
          : row));
  },
} as const satisfies HubEventHandler<RedemptionStatusChangedPayload>;
