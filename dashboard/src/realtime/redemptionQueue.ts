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
      rows?.map((row: Redemption) =>
        row.redemptionId === payload.redemptionId
          ? { ...row, status: payload.status.toLowerCase() } as const satisfies Redemption
          : row));
  },
} as const satisfies HubEventHandler<RedemptionStatusChangedPayload>;
