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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Behavioural proof for <see cref="PasswordAuthService"/> — the native email+password login. Register turns
/// an email + password into a real <see cref="User"/> row with a verifiable hash and a tenant-less session;
/// login proves the hash and resolves an existing channel when the account owns one.
/// </summary>
public sealed class PasswordAuthServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    private static (
        PasswordAuthService Service,
        IApplicationDbContext Db,
        ISessionService Sessions,
        ServiceProvider Provider,
        IServiceScope Scope
    ) Build()
    {
        ServiceCollection services = new();
        string dbName = Guid.NewGuid().ToString();
        services.AddDbContext<AuthDbContext>(o =>
            o.UseSqlite(AuthTestBuilder.SharedDatabase(dbName))
        );
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AuthDbContext>());
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(FixedNow));

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

        ServiceProvider provider = services.BuildServiceProvider();
        IServiceScope scope = provider.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        PasswordAuthService svc = new(
            db,
            sessions,
            scope.ServiceProvider.GetRequiredService<TimeProvider>(),
            NullLogger<PasswordAuthService>.Instance
        );
        return (svc, db, sessions, provider, scope);
    }

    [Fact]
    public async Task RegisterAsync_creates_a_user_with_a_verifiable_hash_and_a_tenant_less_session()
    {
        (PasswordAuthService svc, IApplicationDbContext db, ISessionService sessions, _, _) =
            Build();

        Result<AuthResultDto> result = await svc.RegisterAsync(
            "Streamer@Example.com",
            "correct horse battery",
            new("web", null, null)
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.AccessToken.Should().Be("access-tok");
        result.Value.User.Username.Should().Be("streamer");

        User user = await db.Users.SingleAsync();
        user.Platform.Should().Be("password");
        user.LoginEmail.Should().Be("Streamer@Example.com");
        user.LoginEmailNormalized.Should().Be("streamer@example.com");
        user.PasswordHash.Should().NotBeNullOrEmpty();
        user.PasswordUpdatedAt.Should().Be(FixedNow.UtcDateTime);

        // A brand-new password account has no channel yet — the session must be tenant-less, exactly like a
        // fresh non-Twitch OAuth login, so the client lands on the setup wizard's "connect a platform" step.
        await sessions
            .Received(1)
            .CreateSessionAsync(
                user.Id,
                null,
                Arg.Any<AuthContextDto>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task RegisterAsync_rejects_a_second_account_on_the_same_email()
    {
        (PasswordAuthService svc, _, _, _, _) = Build();
        await svc.RegisterAsync("dup@example.com", "correct horse battery", new("web", null, null));

        Result<AuthResultDto> second = await svc.RegisterAsync(
            "DUP@example.com",
            "a different password",
            new("web", null, null)
        );

        second.IsFailure.Should().BeTrue();
        second.ErrorCode.Should().Be("EMAIL_TAKEN");
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("")]
    public async Task RegisterAsync_rejects_a_malformed_email(string email)
    {
        (PasswordAuthService svc, _, _, _, _) = Build();

        Result<AuthResultDto> result = await svc.RegisterAsync(
            email,
            "correct horse battery",
            new("web", null, null)
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("EMAIL_INVALID");
    }

    [Fact]
    public async Task RegisterAsync_rejects_a_password_below_the_minimum_length()
    {
        (PasswordAuthService svc, _, _, _, _) = Build();

        Result<AuthResultDto> result = await svc.RegisterAsync(
            "short@example.com",
            "1234567",
            new("web", null, null)
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("WEAK_PASSWORD");
    }

    [Fact]
    public async Task LoginAsync_with_the_correct_password_succeeds()
    {
        (PasswordAuthService svc, _, _, _, _) = Build();
        await svc.RegisterAsync(
            "login@example.com",
            "correct horse battery",
            new("web", null, null)
        );

        Result<AuthResultDto> result = await svc.LoginAsync(
            "Login@Example.com",
            "correct horse battery",
            new("web", null, null)
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.User.Username.Should().Be("login");
    }

    [Fact]
    public async Task LoginAsync_with_the_wrong_password_fails_with_the_generic_invalid_credentials_code()
    {
        (PasswordAuthService svc, _, _, _, _) = Build();
        await svc.RegisterAsync(
            "wrongpw@example.com",
            "correct horse battery",
            new("web", null, null)
        );

        Result<AuthResultDto> result = await svc.LoginAsync(
            "wrongpw@example.com",
            "totally different",
            new("web", null, null)
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task LoginAsync_for_an_unknown_email_fails_with_the_SAME_generic_code_as_a_wrong_password()
    {
        (PasswordAuthService svc, _, _, _, _) = Build();

        Result<AuthResultDto> result = await svc.LoginAsync(
            "nobody@example.com",
            "whatever it is",
            new("web", null, null)
        );

        // Never a distinct "no such account" code — that would let a caller enumerate registered emails.
        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task LoginAsync_resolves_the_callers_existing_channel_into_the_session()
    {
        (PasswordAuthService svc, IApplicationDbContext db, ISessionService sessions, _, _) =
            Build();
        await svc.RegisterAsync(
            "owner@example.com",
            "correct horse battery",
            new("web", null, null)
        );
        User user = await db.Users.SingleAsync();

        Channel channel = new()
        {
            OwnerUserId = user.Id,
            TwitchChannelId = "t-1",
            ExternalChannelId = "t-1",
            Name = "owner",
            NameNormalized = "owner",
            IsOnboarded = true,
        };
        db.Channels.Add(channel);
        await db.SaveChangesAsync(CancellationToken.None);

        await svc.LoginAsync("owner@example.com", "correct horse battery", new("web", null, null));

        await sessions
            .Received(1)
            .CreateSessionAsync(
                user.Id,
                channel.Id,
                Arg.Any<AuthContextDto>(),
                Arg.Any<CancellationToken>()
            );
    }
}
