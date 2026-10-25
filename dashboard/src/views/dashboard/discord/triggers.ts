// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { DiscordTrigger } from '@/api/discord';

// The bot lists the triggers it posts for (GET /discord/triggers), so the page never offers one that cannot fire.
// A rule saved earlier for new_clip, schedule or milestone still shows, under the words kept for it here.
const retired = ['new_clip', 'schedule', 'milestone'];

function retiredKey(trigger: string): string | undefined {
  return retired.includes(trigger)
    ? `dashboard.discord.triggers.${trigger}`
    : undefined;
}

// A trigger the page has no words for shows as the bot spells it.
export function triggerLabel(
  t: (key: string) => string,
  trigger: string,
  offered: DiscordTrigger[],
): string {
  const key = offered.find((entry) => entry.name === trigger)?.labelKey ?? retiredKey(trigger);

  return key === undefined
    ? trigger
    : t(key);
}

// The placeholders the bot fills for a trigger, as the message field writes them.
export function triggerPlaceholders(trigger: string, offered: DiscordTrigger[]): string[] {
  return offered.find((entry) => entry.name === trigger)?.placeholders ?? [];
}
