// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Commands.Builtin.Personality;

/// <summary>
/// The old bot's help line for each built-in command (<c>!help &lt;builtin&gt;</c>), one slot per built-in
/// (<see cref="BuiltinResponseSlots.Help.LineFor"/>). The server cannot render a dashboard resource key into chat,
/// so the line lives here, in the reply catalogue: a streamer can edit it per channel, and the dashboard shows
/// its label and description through the schema i18n manifest like every other slot.
/// </summary>
public static partial class ToneTemplateCatalog
{
    // (built-in key, "<usage args> — " free text exactly as the old bot's Help.cs wrote it, after the command name)
    // A method, not a field: partial-class static initialisers run in no fixed order, and Catalog is built first.
    private static (string BuiltinKey, string Line)[] HelpLines() =>
        [
            ("accountage", "— Shows how old your Twitch account is."),
            ("banger", "— Adds the currently playing song to the bangers playlist."),
            ("bansong", "[reason] — (Mod) Bans the current song and skips it."),
            ("commands", "— Lists all available commands for your permission level."),
            ("discord", "— Shows the Discord invite link."),
            ("followage", "— Shows how long you have been following the channel."),
            ("help", "<command> — Shows help info for a specific command."),
            ("leaderboard", "— Displays the top 3 users across various categories."),
            ("lurk", "— Marks you as lurking in chat."),
            ("unlurk", "— Marks you as no longer lurking."),
            ("playlist", "— Gives you the Spotify link to the bangers playlist."),
            ("quote", "[add <text> | #number] — View or add stream quotes."),
            ("skip", "— (Mod) Skips the currently playing song."),
            ("song", "— Shows the current song playing on stream."),
            ("sr", "<spotify url or song name> — Request a song to be added to the queue."),
            ("stats", "[@user] — Shows chat stats for yourself or another user."),
            ("update", "[@user] — Updates user info from Twitch."),
            ("voice", "languages | get <lang> | set <voice> | current — Manage your TTS voice."),
            ("volume", "[0-100] — (Mod) Gets or sets the music volume."),
            ("whisper", "<text> — Whispers your message dramatically."),
        ];

    private static void AddHelpLineSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        foreach ((string builtinKey, string text) in HelpLines())
        {
            string line = $"{{prefix}}{builtinKey} {text}";
            Add(
                catalog,
                BuiltinResponseSlots.Help.Key,
                BuiltinResponseSlots.Help.LineFor(builtinKey),
                variables: ["prefix", "user"],
                informative: [line],
                friendly: [$"@{{user}} good question! {line}"],
                sassy: [$"@{{user}} {line} You could've read the pins, but sure."],
                hype: [$"@{{user}} {line} NOW GO USE IT."],
                chill: [$"@{{user}} {line}"]
            );
        }
    }
}
