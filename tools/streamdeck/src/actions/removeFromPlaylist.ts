// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { action} from "@elgato/streamdeck";
import type { JsonObject } from "@elgato/utils";
import { MusicAction } from "./musicAction.js";

interface PlaylistSettings extends JsonObject {
  playlistId?: string;
}

@action({ UUID: "bot.nomnomzbot.streamdeck.music-remove-from-playlist" })
export class RemoveFromPlaylistAction extends MusicAction<PlaylistSettings> {
  protected readonly actionType = "music_remove_from_playlist";
  protected readonly iconName = "playlist-remove";

  protected override resolveParams(settings: PlaylistSettings): Record<string, unknown> {
    return { playlistId: settings.playlistId ?? "" };
  }
}
