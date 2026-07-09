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
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.Identity;
using NSubstitute;
using Xunit;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Behavioural proof for <see cref="ExternalLoginService"/> (platform-identity §3.3) — the generic non-Twitch
/// login. A proven YouTube identity turns into a user + primary identity (with the vaulted login connection
/// linked) and a tenant-less session, all from the <see cref="ExternalIdentityProof"/>.
/// </summary>
public sealed class ExternalLoginServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task LoginAsync_creates_a_non_twitch_user_identity_and_session()
    {
        ServiceCollection services = new();
        string dbName = Guid.NewGuid().ToString();
        services.AddDbContext<AuthDbContext>(o =>
            o.UseInMemoryDatabase(dbName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        );
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AuthDbContext>());
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(FixedNow));
        services.AddScoped<IUserIdentityService, UserIdentityService>();

        ISessionService sessions = Substitute.For<ISessionService>();
        sessions
            .CreateSessionAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid?>(),
                Arg.Any<AuthContextDto>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new SessionTokensDto(
                        "access-tok",
                        "refresh-tok",
                        FixedNow.UtcDateTime.AddHours(1),
                        FixedNow.UtcDateTime.AddDays(30),
                        Guid.CreateVersion7()
                    )
                )
            );

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        ExternalLoginService svc = new(
            db,
            scope.ServiceProvider.GetRequiredService<IUserIdentityService>(),
            sessions,
            scope.ServiceProvider.GetRequiredService<TimeProvider>()
        );

        Guid connectionId = Guid.CreateVersion7();
        ExternalIdentityProof proof = new(
            "youtube",
            "yt-42",
            "creator",
            "The Creator",
            "https://cdn/y.png",
            connectionId
        );

        Result<AuthResultDto> result = await svc.LoginAsync(
            proof,
            new AuthContextDto("web", null, null)
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.AccessToken.Should().Be("access-tok");
        result.Value.RefreshToken.Should().Be("refresh-tok");
        result.Value.User.Username.Should().Be("creator");

        // A tenant-less session was requested (no channel yet for a fresh non-Twitch user).
        await sessions
            .Received(1)
            .CreateSessionAsync(
                Arg.Any<Guid>(),
                null,
                Arg.Any<AuthContextDto>(),
                Arg.Any<CancellationToken>()
            );

        User user = await db.Users.SingleAsync();
        user.Platform.Should().Be("youtube");
        user.TwitchUserId.Should().BeNull();
        user.Username.Should().Be("creator");

        UserIdentity identity = await db.UserIdentities.SingleAsync();
        identity.Provider.Should().Be("youtube");
        identity.ProviderUserId.Should().Be("yt-42");
        identity.ProviderUsername.Should().Be("creator");
        identity.ProviderDisplayName.Should().Be("The Creator");
        identity.IsPrimary.Should().BeTrue();
        identity.ConnectionId.Should().Be(connectionId);
    }
}
