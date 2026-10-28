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
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Infrastructure.Music;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Proves which providers a channel can use: a live connected <c>IntegrationConnections</c> row counts (the
/// token vault is where YouTube tokens live), a disconnected or deleted one does not, and the legacy
/// <c>Services</c> mirror still works for Spotify.
/// </summary>
public sealed class UsableMusicProvidersTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000ae002");

    [Fact]
    public async Task A_connected_vault_row_makes_youtube_usable_without_a_services_row()
    {
        MusicTestDbContext db = MusicTestDbContext.New();
        db.IntegrationConnections.Add(Connection("youtube", AuthEnums.IntegrationStatus.Connected));
        db.SaveChanges();

        IReadOnlyList<string> usable = await Usable(db);

        usable.Should().Equal("youtube");
    }

    [Fact]
    public async Task A_disconnected_vault_row_does_not_make_youtube_usable()
    {
        MusicTestDbContext db = MusicTestDbContext.New();
        db.IntegrationConnections.Add(
            Connection("youtube", AuthEnums.IntegrationStatus.NeedsReauth)
        );
        db.SaveChanges();

        (await Usable(db)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_soft_deleted_vault_row_does_not_make_youtube_usable()
    {
        MusicTestDbContext db = MusicTestDbContext.New();
        IntegrationConnection row = Connection("youtube", AuthEnums.IntegrationStatus.Connected);
        row.DeletedAt = DateTime.UtcNow;
        db.IntegrationConnections.Add(row);
        db.SaveChanges();

        (await Usable(db)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_vault_row_of_another_channel_does_not_count()
    {
        MusicTestDbContext db = MusicTestDbContext.New();
        IntegrationConnection row = Connection("youtube", AuthEnums.IntegrationStatus.Connected);
        row.BroadcasterId = Guid.NewGuid();
        db.IntegrationConnections.Add(row);
        db.SaveChanges();

        (await Usable(db)).Should().BeEmpty();
    }

    [Fact]
    public async Task The_legacy_services_row_still_makes_spotify_usable()
    {
        MusicTestDbContext db = MusicTestDbContext.New();
        db.Services.Add(
            new()
            {
                Id = Guid.NewGuid().ToString(),
                Name = "spotify",
                BroadcasterId = ChannelId,
                Enabled = true,
                AccessToken = "test-access-token",
            }
        );
        db.SaveChanges();

        IReadOnlyList<string> usable = await Usable(db);

        usable.Should().Equal("spotify");
    }

    private static IntegrationConnection Connection(string provider, string status) =>
        new()
        {
            BroadcasterId = ChannelId,
            Provider = provider,
            Status = status,
        };

    private static async Task<IReadOnlyList<string>> Usable(MusicTestDbContext db)
    {
        IReadOnlyList<IMusicProvider> result = await UsableMusicProviders.ForChannelAsync(
            db,
            [Stub("spotify"), Stub("youtube")],
            ChannelId,
            CancellationToken.None
        );
        return result.Select(p => p.Provider).ToList();
    }

    private static IMusicProvider Stub(string name)
    {
        IMusicProvider provider = Substitute.For<IMusicProvider>();
        provider.Provider.Returns(name);
        provider.WorksWithoutConnectionAsync(Arg.Any<CancellationToken>()).Returns(false);
        return provider;
    }
}
