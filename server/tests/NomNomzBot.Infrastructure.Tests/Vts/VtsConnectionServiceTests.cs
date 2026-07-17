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
using NomNomzBot.Application.Vts.Dtos;
using NomNomzBot.Domain.Vts.Entities;
using NomNomzBot.Infrastructure.Vts;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Vts;

/// <summary>
/// Proves the P.19 custody rules (vtube-studio.md §1/§3): the plugin token is sealed on store (the
/// column never carries plaintext, storing flips <c>Status</c> to <c>authorized</c>) and NO read
/// path returns it — the DTO only says one exists; upserts never touch the token; only the
/// transport-facing accessor opens it; rotate replaces the bridge credential; reads never write and
/// surface the binding defaults.
/// </summary>
public sealed class VtsConnectionServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000fa01");

    private static (VtsConnectionService Sut, VtsTestDbContext Db) Build()
    {
        VtsTestDbContext db = VtsTestDbContext.New();
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
        return (new VtsConnectionService(db, protector), db);
    }

    [Fact]
    public async Task Get_without_a_row_returns_defaults_and_creates_nothing()
    {
        (VtsConnectionService sut, VtsTestDbContext db) = Build();

        Result<VtsConnectionDto> result = await sut.GetAsync(Channel);

        result.IsSuccess.Should().BeTrue();
        result.Value.Mode.Should().Be("direct");
        result.Value.Endpoint.Should().Be("ws://localhost:8001");
        result.Value.HasPluginToken.Should().BeFalse();
        result.Value.Status.Should().Be("unauthorized");
        (await db.VtsConnections.CountAsync()).Should().Be(0, "reads never write");
    }

    [Fact]
    public async Task The_plugin_token_is_sealed_flips_status_and_never_reads_back()
    {
        (VtsConnectionService sut, VtsTestDbContext db) = Build();

        Result stored = await sut.StorePluginTokenAsync(Channel, "vts-granted-token");

        stored.IsSuccess.Should().BeTrue(stored.ErrorMessage);
        VtsConnection row = await db.VtsConnections.SingleAsync();
        row.PluginTokenCipher.Should().Be("sealed(vts-granted-token)");
        row.PluginTokenCipher.Should().NotBe("vts-granted-token");
        row.Status.Should().Be("authorized", "a granted token means the plugin is approved");

        Result<VtsConnectionDto> read = await sut.GetAsync(Channel);
        read.Value.HasPluginToken.Should().BeTrue();
        (await sut.GetPluginTokenForTransportAsync(Channel)).Should().Be("vts-granted-token");
    }

    [Fact]
    public async Task Upserts_never_touch_the_stored_token()
    {
        (VtsConnectionService sut, VtsTestDbContext db) = Build();
        await sut.StorePluginTokenAsync(Channel, "keep-me");

        Result<VtsConnectionDto> upserted = await sut.UpsertAsync(
            Channel,
            new UpsertVtsConnectionRequest
            {
                Mode = "direct",
                Endpoint = "ws://192.168.2.50:8001",
                IsEnabled = true,
            }
        );

        upserted.IsSuccess.Should().BeTrue(upserted.ErrorMessage);
        VtsConnection row = await db.VtsConnections.SingleAsync();
        row.PluginTokenCipher.Should().Be("sealed(keep-me)", "config saves never clear the grant");
        row.Endpoint.Should().Be("ws://192.168.2.50:8001");
        row.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Rotate_replaces_the_bridge_credential()
    {
        (VtsConnectionService sut, VtsTestDbContext db) = Build();
        await sut.UpsertAsync(
            Channel,
            new UpsertVtsConnectionRequest { Mode = "bridge", IsEnabled = true }
        );

        Result<VtsConnectionDto> first = await sut.RotateBridgeTokenAsync(Channel);
        string tokenBefore = (await db.VtsConnections.SingleAsync()).BridgeToken!;
        Result<VtsConnectionDto> second = await sut.RotateBridgeTokenAsync(Channel);
        string tokenAfter = (await db.VtsConnections.SingleAsync()).BridgeToken!;

        first.Value.HasBridgeToken.Should().BeTrue();
        second.IsSuccess.Should().BeTrue(second.ErrorMessage);
        tokenAfter.Should().NotBe(tokenBefore, "the old credential stops authenticating");
    }
}
