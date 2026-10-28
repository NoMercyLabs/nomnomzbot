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

import { communityFirstPageKey, memberKey } from '@/api/community';
import { communityListsKey, viewerProfilesKey } from '@/api/people';

import type { HubEventHandler } from './hubEvent';
import type { ModActionPayload } from './payloads';

// A moderation action changes the member's standing, not a full member record, so the member is fetched again.
export const communityHandlers: [HubEventHandler<ModActionPayload>] = [
  {
    event: 'ModAction',
    handle: ({ queries, channelId }, action) => {
      void queries.invalidateQueries({ queryKey: memberKey(channelId, action.targetUserId) });
      void queries.invalidateQueries({ queryKey: communityFirstPageKey(channelId) });
      // The payload names the Twitch id; profiles are keyed by the internal id, so every profile of the channel is marked.
      void queries.invalidateQueries({ queryKey: viewerProfilesKey(channelId) });
      void queries.invalidateQueries({ queryKey: communityListsKey(channelId) });
    },
  },
];
