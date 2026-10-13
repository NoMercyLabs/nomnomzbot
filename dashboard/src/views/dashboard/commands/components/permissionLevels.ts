// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// The role rungs a command can ask for, lowest first. There is no Followers rung: the bot has none.
export const permissionLevels: readonly string[] = [
  'Everyone',
  'Subscriber',
  'Vip',
  'Artist',
  'Moderator',
  'LeadModerator',
  'Editor',
  'Broadcaster',
]
