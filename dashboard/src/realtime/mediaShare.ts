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

import {
  mediaSharePlayerKey,
  mediaShareQueueKey,
  type MediaSharePlayerState,
} from '@/api/mediaShare';

import type { HubEventHandler } from './hubEvent';
import type { ChannelEventPayload } from './payloads';

// MediaPlayerStateBroadcastHandler.cs: the overlay player reported its state. The page follows the stream with
// it, and the queue moves on when an item starts, ends or fails.
export const mediaShareHandlers: HubEventHandler<ChannelEventPayload>[] = [
  {
    event: 'ChannelEvent',
    handle: ({ queries, channelId }, event) => {
      if (event.type !== 'media_share_player_state') return;

      queries.setQueryData<MediaSharePlayerState>(
        mediaSharePlayerKey(channelId),
        event.data as MediaSharePlayerState,
      );

      void queries.invalidateQueries({ queryKey: mediaShareQueueKey(channelId) });
    },
  },
];
