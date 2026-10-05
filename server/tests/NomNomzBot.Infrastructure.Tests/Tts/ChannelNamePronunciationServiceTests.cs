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
using NomNomzBot.Application.Tts.Dtos;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.Tests.Platform.Templating;
using NomNomzBot.Infrastructure.Tts;

namespace NomNomzBot.Infrastructure.Tests.Tts;

/// <summary>
/// The channels table is the source of the spoken form of a channel name: only channels that set a pronunciation
/// are listed, each as its login plus the pronunciation, and an empty value counts as unset.
/// </summary>
public sealed class ChannelNamePronunciationServiceTests
{
    private static User NewOwner(string name) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            TwitchUserId = name,
            Username = name,
            UsernameNormalized = name.ToLowerInvariant(),
            DisplayName = name,
        };

    private static Channel NewChannel(User owner, string? pronunciation) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            OwnerUserId = owner.Id,
            Name = owner.Username,
            NameNormalized = owner.UsernameNormalized,
            ExternalChannelId = owner.Username,
            UsernamePronunciation = pronunciation,
        };

    [Fact]
    public async Task List_returns_only_channels_with_a_pronunciation_as_name_and_spoken_form()
    {
        using PronounGrammarTestDbContext db = PronounGrammarTestDbContext.New();
        User jd = NewOwner("xX_JD_Xx");
        User plain = NewOwner("plainname");
        User blank = NewOwner("blankname");
        db.Users.AddRange(jd, plain, blank);
        db.Channels.AddRange(
            NewChannel(jd, "Jaydee"),
            NewChannel(plain, null),
            NewChannel(blank, "")
        );
        await db.SaveChangesAsync();
        ChannelNamePronunciationService sut = new(db);

        IReadOnlyList<ChannelNamePronunciation> listed = await sut.ListAsync();

        listed.Should().Equal(new ChannelNamePronunciation("xX_JD_Xx", "Jaydee"));
    }
}
