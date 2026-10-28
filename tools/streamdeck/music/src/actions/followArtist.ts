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

import { action } from "@elgato/streamdeck";
import { MusicAction } from "./musicAction.js";

@action({ UUID: "bot.nomnomzbot.streamdeck.music.music-follow-artist" })
export class FollowArtistAction extends MusicAction {
  protected readonly actionType = "music_follow_artist";
  protected readonly iconName = "follow";
}
