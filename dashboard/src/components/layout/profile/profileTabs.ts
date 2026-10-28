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

import { merge } from '@/lib/array';

// The tabs of a person's profile. The header already shows every count, so no tab repeats them. Points and Data
// come from the bot's stored profile, so they show once it loaded. Settings is always last: it changes how the bot
// treats the person, where the other tabs only show. The public view shows the first three only.
export const PUBLIC_TABS = ['overview', 'chat', 'emotes'] as const;
export const BASE_TABS = ['overview', 'chat', 'emotes', 'moderation'] as const;
export const PROFILE_TABS = ['points', 'data'] as const;
export const SETTINGS_TABS = ['settings'] as const;
export const ALL_TABS = merge(BASE_TABS, PROFILE_TABS, SETTINGS_TABS);

export type ProfileTab = (typeof ALL_TABS)[number];
