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
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Obs.Dtos;
using NomNomzBot.Domain.Obs.Entities;
using NomNomzBot.Infrastructure.Obs;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Obs;

/// <summary>
/// Proves the P.14 secret-custody rules (obs-control.md §1/§9): the OBS-WS password is sealed on
/// write (the stored column never carries the plaintext) and NO read path returns it — the DTO only
/// says whether one exists; null keeps and empty clears the stored secret; only the transport-facing
/// accessor can open it. Bridge setup mints the credential once and rotate replaces it so the old
/// setup URL dies. Reads never write; defaults surface when nothing is stored.
/// </summary>
public sealed class ObsConnectionServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000f601");
    private const string Backend = "https://bot-dev-api.nomercy.tv";

    private static (ObsConnectionService Sut, ObsTestDbContext Db) Build()
    {
        ObsTestDbContext db = ObsTestDbContext.New();

        // Reversible fake vault: seal = wrap, open = unwrap — lets tests prove what was stored
        // without a real DEK chain.
        ITokenProtector protector = Substitute.For<ITokenProtector>();
        protector
            .ProtectAsync(
                Arg.Any<string>(),
                Arg.Any<TokenProtectionContext>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ci => $"sealed({ci.ArgAt<string>(0)})");
        protector
            .TryUnprotectAsync(
                Arg.Any<string?>(),
                Arg.Any<TokenProtectionContext>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ci =>
            {
                string? envelope = ci.ArgAt<string?>(0);
                return envelope is not null && envelope.StartsWith("sealed(")
                    ? envelope["sealed(".Length..^1]
                    : null;
            });

        return (new ObsConnectionService(db, protector), db);
    }

    [Fact]
    public async Task Get_without_a_row_returns_defaults_and_creates_nothing()
    {
        (ObsConnectionService sut, ObsTestDbContext db) = Build();

        Result<ObsConnectionDto> result = await sut.GetAsync(Channel);

        result.IsSuccess.Should().BeTrue();
        result.Value.Mode.Should().Be("direct");
        result.Value.Host.Should().Be("127.0.0.1");
        result.Value.Port.Should().Be(4455);
        result.Value.HasPassword.Should().BeFalse();
        result.Value.IsEnabled.Should().BeFalse();
        (await db.ObsConnections.CountAsync()).Should().Be(0, "reads never write");
    }

    [Fact]
    public async Task The_password_is_sealed_at_rest_and_never_readable_through_the_api()
    {
        (ObsConnectionService sut, ObsTestDbContext db) = Build();

        Result<ObsConnectionDto> upserted = await sut.UpsertAsync(
            Channel,
            new UpsertObsConnectionRequest
            {
                Mode = "direct",
                Password = "obs-ws-secret",
                IsEnabled = true,
            }
        );

        upserted.IsSuccess.Should().BeTrue(upserted.ErrorMessage);
        upserted.Value.HasPassword.Should().BeTrue();

        ObsConnection row = await db.ObsConnections.SingleAsync();
        row.PasswordCipher.Should().Be("sealed(obs-ws-secret)");
        row.PasswordCipher.Should().NotBe("obs-ws-secret", "the column never carries plaintext");

        // The management read exposes only the presence flag; the transport accessor opens it.
        Result<ObsConnectionDto> read = await sut.GetAsync(Channel);
        read.Value.HasPassword.Should().BeTrue();
        (await sut.GetPasswordForTransportAsync(Channel)).Should().Be("obs-ws-secret");
    }

    [Fact]
    public async Task Password_null_keeps_and_empty_clears_the_stored_secret()
    {
        (ObsConnectionService sut, ObsTestDbContext db) = Build();
        await sut.UpsertAsync(
            Channel,
            new UpsertObsConnectionRequest { Mode = "direct", Password = "keep-me" }
        );

        // Null = leave the secret alone (an ordinary settings save never wipes it).
        await sut.UpsertAsync(
            Channel,
            new UpsertObsConnectionRequest { Mode = "direct", Host = "192.168.2.50" }
        );
        ObsConnection afterKeep = await db.ObsConnections.SingleAsync();
        afterKeep.PasswordCipher.Should().Be("sealed(keep-me)");
        afterKeep.Host.Should().Be("192.168.2.50");

        // Empty string = deliberate clear.
        await sut.UpsertAsync(
            Channel,
            new UpsertObsConnectionRequest { Mode = "direct", Password = "" }
        );
        (await db.ObsConnections.SingleAsync()).PasswordCipher.Should().BeNull();
        (await sut.GetPasswordForTransportAsync(Channel)).Should().BeNull();
    }

    [Fact]
    public async Task Bridge_setup_mints_once_and_rotate_replaces_the_credential()
    {
        (ObsConnectionService sut, ObsTestDbContext db) = Build();

        Result<ObsBridgeSetupDto> first = await sut.GetBridgeSetupAsync(Channel, Backend);
        Result<ObsBridgeSetupDto> second = await sut.GetBridgeSetupAsync(Channel, Backend);

        first.IsSuccess.Should().BeTrue(first.ErrorMessage);
        first.Value.BridgeUrl.Should().StartWith($"{Backend}/obs-bridge?token=");
        second.Value.BridgeUrl.Should().Be(first.Value.BridgeUrl, "setup is stable until rotated");

        string tokenBefore = (await db.ObsConnections.SingleAsync()).BridgeToken!;
        Result<ObsBridgeSetupDto> rotated = await sut.RotateBridgeTokenAsync(Channel, Backend);

        rotated.IsSuccess.Should().BeTrue(rotated.ErrorMessage);
        string tokenAfter = (await db.ObsConnections.SingleAsync()).BridgeToken!;
        tokenAfter.Should().NotBe(tokenBefore, "the old setup URL stops authenticating");
        rotated.Value.BridgeUrl.Should().Contain(tokenAfter);
    }
}
