// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
