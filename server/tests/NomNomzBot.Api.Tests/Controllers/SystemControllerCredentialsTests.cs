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
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Identity.Services;
using NSubstitute;
using ConfigEntity = NomNomzBot.Domain.Platform.Entities.Configuration;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves the Twitch app secret is OPTIONAL at setup (onboarding-setup.md): the bot logs in secret-free via the
/// Device Code Flow on the client id alone, so an empty secret must store ONLY the client id — never an empty
/// ciphertext that would make the system look secret-configured and then fail the redirect exchange instead of
/// falling back to device code. A supplied secret is still sealed and stored.
/// </summary>
public sealed class SystemControllerCredentialsTests
{
    // All ISystemCredentialsProvider reads return null ⇒ ComputeSetupState sees nothing configured ⇒ setup is
    // not complete ⇒ the save runs without the post-setup admin gate (the first-run window).
    private static SystemController Build(ApiTestDbContext db, ITokenProtector protector) =>
        new(
            Substitute.For<IAuthService>(),
            db,
            new ConfigurationBuilder().Build(),
            protector,
            Substitute.For<ISystemCredentialsProvider>(),
            Substitute.For<IHostEnvironment>(),
            Substitute.For<ITwitchOAuthStateService>()
        );

    [Fact]
    public async Task SaveTwitchCredentials_WithNoSecret_StoresOnlyTheClientId_AndNeverSeals()
    {
        ApiTestDbContext db = ApiTestDbContext.New();
        ITokenProtector protector = Substitute.For<ITokenProtector>();
        SystemController controller = Build(db, protector);

        IActionResult result = await controller.SaveTwitchCredentials(
            new SystemController.SaveTwitchCredentialRequest("twitch-public-id", "", null),
            default
        );

        result.Should().BeOfType<OkObjectResult>();
        List<ConfigEntity> rows = await db.Configurations.ToListAsync();
        rows.Should()
            .ContainSingle(c => c.Key == "twitch.client_id" && c.Value == "twitch-public-id");
        rows.Should()
            .NotContain(c => c.Key == "twitch.client_secret", "an empty secret is never persisted");
        // No vault work at all — no empty ciphertext masquerading as a configured secret.
        await protector
            .DidNotReceive()
            .ProtectAsync(
                Arg.Any<string>(),
                Arg.Any<TokenProtectionContext>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task SaveTwitchCredentials_WithASecret_SealsAndStoresBoth()
    {
        ApiTestDbContext db = ApiTestDbContext.New();
        ITokenProtector protector = Substitute.For<ITokenProtector>();
        protector
            .ProtectAsync(
                Arg.Any<string>(),
                Arg.Any<TokenProtectionContext>(),
                Arg.Any<CancellationToken>()
            )
            .Returns("sealed-envelope");
        SystemController controller = Build(db, protector);

        IActionResult result = await controller.SaveTwitchCredentials(
            new SystemController.SaveTwitchCredentialRequest("twitch-id", "the-secret", null),
            default
        );

        result.Should().BeOfType<OkObjectResult>();
        List<ConfigEntity> rows = await db.Configurations.ToListAsync();
        rows.Should().Contain(c => c.Key == "twitch.client_id" && c.Value == "twitch-id");
        // The supplied secret is sealed under the vault (the secure column), never stored in plaintext.
        rows.Should()
            .ContainSingle(c =>
                c.Key == "twitch.client_secret" && c.SecureValue == "sealed-envelope"
            );
        await protector
            .Received(1)
            .ProtectAsync(
                "the-secret",
                Arg.Any<TokenProtectionContext>(),
                Arg.Any<CancellationToken>()
            );
    }
}
