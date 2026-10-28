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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Templating;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating;

/// <summary>
/// <c>{user.accountAge}</c> is the age of the viewer's account at the provider (Twitch <c>created_at</c>), never
/// the age of the bot's own User row: a viewer the bot first saw yesterday with a seven-year-old account reads
/// "7 years".
/// </summary>
public sealed class AccountAgeTemplateResolverTests
{
    private static readonly Guid Channel = Guid.Parse("0192b400-0000-7000-9000-00000000e002");
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset TwitchCreated = new(2019, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly ITwitchUsersApi _users = Substitute.For<ITwitchUsersApi>();
    private readonly PronounGrammarTestDbContext _db = PronounGrammarTestDbContext.New();

    private TemplateResolver BuildResolver(DateTime? accountCreatedAt)
    {
        _db.Users.Add(
            new()
            {
                TwitchUserId = "777",
                Username = "zed",
                UsernameNormalized = "zed",
                DisplayName = "Zed",
                CreatedAt = Now.UtcDateTime.AddDays(-1),
                AccountCreatedAt = accountCreatedAt,
            }
        );
        _db.SaveChanges();

        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(_db);
        services.AddSingleton(_users);
        ServiceProvider provider = services.BuildServiceProvider();

        return new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<IChannelRegistry>(),
            NullLogger<TemplateResolver>.Instance,
            new FakeTimeProvider(Now)
        );
    }

    [Fact]
    public async Task Reads_the_provider_account_date_not_the_bot_row_date()
    {
        TemplateResolver resolver = BuildResolver(TwitchCreated.UtcDateTime);

        string resolved = await resolver.ResolveAsync(
            "{user.accountAge}",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["user.id"] = "777",
            },
            Channel
        );

        resolved.Should().Be("7 years");
    }

    [Fact]
    public async Task Fetches_the_account_date_from_twitch_when_it_was_never_stored_and_keeps_it()
    {
        _users
            .GetUsersByIdsAsync(
                Arg.Is<IReadOnlyList<string>>(ids => ids.Contains("777")),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success<IReadOnlyList<TwitchUser>>([
                    new("777", "zed", "Zed", "", "", "", "", "", 0, TwitchCreated),
                ])
            );
        TemplateResolver resolver = BuildResolver(accountCreatedAt: null);

        string resolved = await resolver.ResolveAsync(
            "{user.accountAge}",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["user.id"] = "777",
            },
            Channel
        );

        resolved.Should().Be("7 years");
        User stored = _db.Users.Single(u => u.TwitchUserId == "777");
        stored.AccountCreatedAt.Should().Be(TwitchCreated.UtcDateTime);
    }
}
