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

// vue-i18n reads a dot as a path step, so the key spells the trigger with underscores.
// A trigger the page does not know shows as the bot spells it.
export function triggerLabel(t: (key: string) => string, trigger: string): string {
  return (discordTriggers as readonly string[]).includes(trigger)
    ? t(`dashboard.discord.triggers.${trigger.replaceAll('.', '_')}`)
    : trigger;
}
