// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { QueryClient } from '@tanstack/vue-query';

export type HubEventContext = {
  queries: QueryClient;
  // The channel this connection joined; null on the operator hub.
  channelId: string | null;
};

// One hub event and what it does to the cache. The area files (channels.ts, community.ts) list these.
export type HubEventHandler<TPayload> = {
  event: string;
  handle: (context: HubEventContext, payload: TPayload) => void;
};
