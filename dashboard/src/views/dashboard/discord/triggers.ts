// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// The triggers a Discord rule can be set up for here: the ones the bot names itself (DiscordConfigDefaults) and posts
// for by itself. A rule the bot holds for another trigger still shows, under the name the bot gives it.
export const discordTriggers = ['go_live'] as const;

const labeled = ['go_live', 'new_clip', 'schedule', 'milestone'];

// A trigger the page has no words for shows as the bot spells it.
export function triggerLabel(t: (key: string) => string, trigger: string): string {
  return labeled.includes(trigger)
    ? t(`dashboard.discord.triggers.${trigger}`)
    : trigger;
}
