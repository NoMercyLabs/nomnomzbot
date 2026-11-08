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

import nmplayer from '@nomercy-entertainment/nomercy-video-player';
import { TwitchPlugin } from '@nomercy-entertainment/nomercy-video-player/plugins/twitch';

// The media-share overlay widget plays Twitch clips through the published NoMercy video player. This file is
// the entry of one browser script (vite.overlay-player.config.ts builds it to /vendor/nomercy-player.js), so
// the widget loads the player from the bot's own origin and no third-party CDN is involved at runtime.
Object.assign(window, { NomNomzPlayer: { nmplayer, TwitchPlugin } });
