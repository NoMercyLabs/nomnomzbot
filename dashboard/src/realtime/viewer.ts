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

import type { HubEventHandler } from './hubEvent';
import type { ChannelEventPayload } from './payloads';

// A viewer page has no joined management channel, so its keys are matched by shape: the channel lookup
// (open or closed) and the viewer's own queue.
const queueChanged = {
  event: 'ChannelEvent',
  handle: ({ queries }, event) => {
    if (event.type !== 'sr_queue_changed') return;

    void queries.invalidateQueries({
      predicate: ({ queryKey }) =>
        (queryKey[0] === 'viewer' && queryKey[1] === 'channel') ||
        (queryKey[0] === 'channels' && queryKey[2] === 'viewer'),
    });
  },
} as const satisfies HubEventHandler<ChannelEventPayload>;

export const viewerHandlers = [queueChanged] as const;
