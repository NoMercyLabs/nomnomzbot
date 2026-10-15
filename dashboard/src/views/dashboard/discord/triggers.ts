// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// The triggers a Discord rule can listen to, go-live first. A rule the bot holds for another trigger still shows.
export const discordTriggers = [
  'stream.online',
  'stream.offline',
  'channel.follow',
  'channel.subscribe',
  'channel.subscription.gift',
  'channel.cheer',
  'channel.raid',
  'channel.poll.begin',
  'channel.prediction.begin',
] as const;
