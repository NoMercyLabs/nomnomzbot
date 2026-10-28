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

import { action} from "@elgato/streamdeck";
import type { JsonObject } from "@elgato/utils";
import { MusicAction } from "./musicAction.js";

interface PlaylistSettings extends JsonObject {
  playlistId?: string;
}

@action({ UUID: "bot.nomnomzbot.streamdeck.music.music-remove-from-playlist" })
export class RemoveFromPlaylistAction extends MusicAction<PlaylistSettings> {
  protected readonly actionType = "music_remove_from_playlist";
  protected readonly iconName = "playlist-remove";

  protected override resolveParams(settings: PlaylistSettings): Record<string, unknown> {
    return { playlistId: settings.playlistId ?? "" };
  }
}
