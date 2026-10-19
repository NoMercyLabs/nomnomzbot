// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Application.Tests.Commands;

/// <summary>
/// A viewer refused as "not music" must be told why, in every mood: the video is outside YouTube's Music
/// category and not from an official artist (- Topic) channel, and what to send instead.
/// </summary>
public sealed class NotMusicReplyReasonTests
{
    public static TheoryData<string> Tones =>
        new()
        {
            PersonalityTone.Informative,
            PersonalityTone.Friendly,
            PersonalityTone.Sassy,
            PersonalityTone.Hype,
            PersonalityTone.Chill,
        };

    [Theory]
    [MemberData(nameof(Tones))]
    public void Every_mood_variant_names_the_reason_and_what_to_send_instead(string tone)
    {
        IReadOnlyList<string> variants = ToneTemplateCatalog.Get(
            tone,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.NotMusic
        );

        variants.Should().NotBeEmpty();
        foreach (string variant in variants)
        {
            string text = variant.ToLowerInvariant();
            text.Should().Contain("music category", variant);
            text.Should().Contain("official artist", variant);
            text.Should().Contain("- topic", variant);
            text.Should().Contain("youtube music", variant);
            text.Should().Contain("spotify", variant);
            text.Should().Contain("song name", variant);
        }
    }
}
