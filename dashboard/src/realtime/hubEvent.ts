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

import type { QueryClient } from '@tanstack/vue-query';
import type { KeyParts } from '@/lib/array';

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

// Builds the key of one page query for a channel; the realtime layer marks that query stale.
export type QueryKeyBuilder = (channelId: string | null) => KeyParts;
