// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
