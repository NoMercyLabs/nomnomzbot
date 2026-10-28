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

import { useChannelAccess, type ChannelAccess } from '@/api/access';

import { useCurrentChannel } from './useCurrentChannel';

// The way to gate an element (a tab, panel or button): `const { can } = useCan()`, then
// `v-if="can('commands:write')"`. Page access is gated by meta.action on the route instead.
// Until the bot has answered, `can` is false for every key, so nothing shows that cannot be used.
export function useCan(): ChannelAccess {
  const { channelId } = useCurrentChannel();

  return useChannelAccess(channelId);
}
