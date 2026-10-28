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

using FluentAssertions;
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Infrastructure.Chat.Providers;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// Proves 7TV, BTTV and FFZ emotes carry which set they came from (channel or global) and the uploader exactly as the
/// provider returns it, with the provider's public profile link. An emote the provider gives no uploader for has none.
/// </summary>
public sealed class EmoteOwnerParseTests
{
    [Fact]
    public void SevenTv_channel_emote_carries_the_uploader_and_profile_url()
    {
        const string json = """
            { "emote_set": { "emotes": [ {
                "id": "e1", "name": "PepeLaugh", "flags": 0,
                "data": { "animated": false, "flags": 0,
                  "owner": { "id": "01OWNER", "username": "uploader", "display_name": "Uploader" },
                  "host": { "url": "//cdn.7tv.app/emote/e1", "files": [ { "name": "1x.webp", "format": "WEBP" } ] } }
            } ] } }
            """;

        ChatEmote emote = SevenTvEmoteProvider.ParseUser(json).Single();

        emote.Type.Should().Be("channel");
        emote
            .Owner.Should()
            .Be(
                new ChatEmoteOwner(
                    "01OWNER",
                    "uploader",
                    "Uploader",
                    "https://7tv.app/users/01OWNER"
                )
            );
    }

    [Fact]
    public void SevenTv_global_emote_is_typed_global()
    {
        const string json = """
            { "emotes": [ {
                "id": "e2", "name": "EZ", "flags": 0,
                "data": { "animated": false, "flags": 0,
                  "host": { "url": "//cdn.7tv.app/emote/e2", "files": [ { "name": "1x.webp", "format": "WEBP" } ] } }
            } ] }
            """;

        ChatEmote emote = SevenTvEmoteProvider.ParseGlobal(json).Single();

        emote.Type.Should().Be("global");
        emote.Owner.Should().BeNull();
    }

    [Fact]
    public void Bttv_shared_emote_carries_the_uploader_and_a_channel_emote_only_the_user_id()
    {
        const string json = """
            {
              "channelEmotes": [ { "id": "c1", "code": "chanOne", "imageType": "png", "animated": false, "userId": "bttvOwner" } ],
              "sharedEmotes": [ { "id": "s1", "code": "EZ", "imageType": "png", "animated": false,
                "user": { "id": "bttvUp", "name": "helloboat", "displayName": "HelloBoat", "providerId": "39819556" } } ]
            }
            """;

        IReadOnlyList<ChatEmote> result = BttvEmoteProvider.ParseChannel(json);

        ChatEmote shared = result.Single(e => e.Code == "EZ");
        shared.Type.Should().Be("channel");
        shared
            .Owner.Should()
            .Be(
                new ChatEmoteOwner(
                    "bttvUp",
                    "helloboat",
                    "HelloBoat",
                    "https://betterttv.com/users/bttvUp"
                )
            );
        result
            .Single(e => e.Code == "chanOne")
            .Owner.Should()
            .Be(
                new ChatEmoteOwner("bttvOwner", null, null, "https://betterttv.com/users/bttvOwner")
            );
    }

    [Fact]
    public void Bttv_global_emote_is_typed_global()
    {
        const string json =
            """[ { "id": "g1", "code": "SoSnowy", "imageType": "png", "animated": false, "userId": "x" } ]""";

        BttvEmoteProvider.ParseGlobal(json).Single().Type.Should().Be("global");
    }

    [Fact]
    public void Ffz_emote_carries_the_artist_apart_from_the_owner()
    {
        const string json = """
            { "sets": { "1": { "emoticons": [ {
                "id": 78, "name": "ArtEmote", "modifier": false,
                "owner": { "_id": 2, "name": "dansalvato", "display_name": "DanSalvato" },
                "artist": { "_id": 9, "name": "drawer", "display_name": "Drawer" },
                "urls": { "1": "//cdn.frankerfacez.com/emote/78/1" } },
                { "id": 79, "name": "NoArt", "modifier": false,
                "owner": { "_id": 2, "name": "dansalvato", "display_name": "DanSalvato" },
                "artist": null,
                "urls": { "1": "//cdn.frankerfacez.com/emote/79/1" } } ] } } }
            """;

        IReadOnlyList<ChatEmote> result = FfzEmoteProvider.ParseRoom(json);

        result
            .Single(e => e.Code == "ArtEmote")
            .Artist.Should()
            .Be(new ChatEmoteOwner("9", "drawer", "Drawer", "https://www.frankerfacez.com/drawer"));
        result.Single(e => e.Code == "NoArt").Artist.Should().BeNull();
    }

    [Fact]
    public void Ffz_room_emote_carries_the_uploader_and_profile_url()
    {
        const string json = """
            { "sets": { "1": { "emoticons": [ {
                "id": 77, "name": "OMEGALUL", "modifier": false,
                "owner": { "_id": 2, "name": "dansalvato", "display_name": "DanSalvato" },
                "urls": { "1": "//cdn.frankerfacez.com/emote/77/1" } } ] } } }
            """;

        ChatEmote emote = FfzEmoteProvider.ParseRoom(json).Single();

        emote.Type.Should().Be("channel");
        emote
            .Owner.Should()
            .Be(
                new ChatEmoteOwner(
                    "2",
                    "dansalvato",
                    "DanSalvato",
                    "https://www.frankerfacez.com/dansalvato"
                )
            );
    }
}
