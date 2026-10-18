// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
