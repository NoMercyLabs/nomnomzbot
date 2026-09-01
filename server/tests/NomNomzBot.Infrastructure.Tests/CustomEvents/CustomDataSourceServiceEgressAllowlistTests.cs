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
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.CustomEvents.Services;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Infrastructure.CustomEvents;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.CustomEvents;

/// <summary>
/// Proves the S100b save-time SSRF gate: <see cref="CustomDataSourceService.CreateAsync"/> and
/// <see cref="CustomDataSourceService.UpdateAsync"/> reuse the same H.7 egress-allowlist check
/// <c>CustomDataPollService</c> re-applies at fetch time, but at SAVE time — a disallowed host is rejected
/// with <c>Result.Failure</c> and never persisted, instead of saving and only failing later when polled.
/// </summary>
public sealed class CustomDataSourceServiceEgressAllowlistTests
{
    private static readonly Guid Broadcaster = Guid.CreateVersion7();

    private static CustomDataSourceService Build(CustomDataSourceServiceTestDbContext db) =>
        new(db, Substitute.For<ITokenProtector>(), Substitute.For<ICustomDataIngestService>(), []);

    private static UpsertCustomDataSourceRequest Request(string endpointUrl) =>
        new(
            Name: "sensor",
            DisplayName: "Sensor Feed",
            SourceKind: "poll",
            PresetKey: null,
            EndpointUrl: endpointUrl,
            AuthSecret: null,
            FieldMap: new Dictionary<string, string>(),
            PollIntervalSeconds: 30,
            IsEnabled: true
        );

    [Fact]
    public async Task CreateAsync_rejects_a_disallowed_host_and_persists_nothing()
    {
        CustomDataSourceServiceTestDbContext db = CustomDataSourceServiceTestDbContext.New();
        // Only "allowed.example.com" is on the H.7 allowlist — "evil.example.com" is not.
        db.HttpEgressAllowlists.Add(
            new HttpEgressAllowlist
            {
                BroadcasterId = Broadcaster,
                Fqdn = "allowed.example.com",
                IsEnabled = true,
            }
        );
        await db.SaveChangesAsync();

        CustomDataSourceService service = Build(db);

        Result<CustomDataSourceDto> result = await service.CreateAsync(
            Broadcaster,
            Guid.CreateVersion7(),
            Request("https://evil.example.com/data")
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("EGRESS_NOT_ALLOWED");
        // Not saved to the DB — the rejection happens before Add/SaveChanges.
        db.CustomDataSources.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_accepts_an_allowed_host_and_persists_it()
    {
        CustomDataSourceServiceTestDbContext db = CustomDataSourceServiceTestDbContext.New();
        db.HttpEgressAllowlists.Add(
            new HttpEgressAllowlist
            {
                BroadcasterId = Broadcaster,
                Fqdn = "allowed.example.com",
                IsEnabled = true,
            }
        );
        await db.SaveChangesAsync();

        CustomDataSourceService service = Build(db);

        Result<CustomDataSourceDto> result = await service.CreateAsync(
            Broadcaster,
            Guid.CreateVersion7(),
            Request("https://allowed.example.com/data")
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.EndpointUrl.Should().Be("https://allowed.example.com/data");
        db.CustomDataSources.Should().ContainSingle(s => s.Id == result.Value.Id);
    }

    [Fact]
    public async Task UpdateAsync_rejects_changing_the_endpoint_to_a_disallowed_host()
    {
        CustomDataSourceServiceTestDbContext db = CustomDataSourceServiceTestDbContext.New();
        db.HttpEgressAllowlists.Add(
            new HttpEgressAllowlist
            {
                BroadcasterId = Broadcaster,
                Fqdn = "allowed.example.com",
                IsEnabled = true,
            }
        );
        await db.SaveChangesAsync();

        CustomDataSourceService service = Build(db);
        Result<CustomDataSourceDto> created = await service.CreateAsync(
            Broadcaster,
            Guid.CreateVersion7(),
            Request("https://allowed.example.com/data")
        );
        created.IsSuccess.Should().BeTrue();

        Result<CustomDataSourceDto> updated = await service.UpdateAsync(
            Broadcaster,
            created.Value.Id,
            Guid.CreateVersion7(),
            Request("https://evil.example.com/data")
        );

        updated.IsFailure.Should().BeTrue();
        updated.ErrorCode.Should().Be("EGRESS_NOT_ALLOWED");
        // The original allowed endpoint must survive untouched — the update never wrote through.
        (await db.CustomDataSources.FindAsync(created.Value.Id))!
            .EndpointUrl.Should()
            .Be("https://allowed.example.com/data");
    }
}
