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
