// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
