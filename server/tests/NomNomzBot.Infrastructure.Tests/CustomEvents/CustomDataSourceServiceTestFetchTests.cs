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
using NomNomzBot.Domain.CustomEvents.Entities;
using NomNomzBot.Infrastructure.CustomEvents;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.CustomEvents;

/// <summary>
/// Proves the S100-KEYPICKER-TESTFETCH test-fetch path (custom-events.md): a one-off GET against the source's
/// configured endpoint, fired through the same SSRF-gated <see cref="ICustomDataEgressFetcher"/> seam the poll
/// ingress uses, returns the real fetched JSON plus a flattened list of field-map key-paths matching that JSON's
/// actual shape — so the dashboard can render a key picker instead of making the operator guess JSONPath syntax.
/// </summary>
public sealed class CustomDataSourceServiceTestFetchTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000000e02");

    private static (
        CustomDataSourceService Sut,
        AuthDbContext Db,
        ICustomDataEgressFetcher Fetcher
    ) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ITokenProtector protector = Substitute.For<ITokenProtector>();
        ICustomDataIngestService ingest = Substitute.For<ICustomDataIngestService>();
        ICustomDataEgressFetcher fetcher = Substitute.For<ICustomDataEgressFetcher>();

        CustomDataSourceService sut = new(db, protector, ingest, fetcher, []);
        return (sut, db, fetcher);
    }

    private static async Task<Guid> SeedSourceAsync(
        AuthDbContext db,
        string endpointUrl = "https://api.example.com/heart"
    )
    {
        CustomDataSource source = new()
        {
            BroadcasterId = Channel,
            Name = "heartrate",
            DisplayName = "Heart Rate",
            SourceKind = "poll",
            EndpointUrl = endpointUrl,
            FieldMapJson = "{}",
            IsEnabled = true,
        };
        db.CustomDataSources.Add(source);
        await db.SaveChangesAsync();
        return source.Id;
    }

    [Fact]
    public async Task A_successful_fetch_returns_the_raw_body_and_every_leaf_key_path()
    {
        (CustomDataSourceService sut, AuthDbContext db, ICustomDataEgressFetcher fetcher) = Build();
        Guid id = await SeedSourceAsync(db);

        const string body = """{"bpm":128,"meta":{"device":"pulsoid","tags":["a","b"]}}""";
        fetcher
            .FetchAsync(
                Channel,
                "https://api.example.com/heart",
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(CustomDataEgressFetchResult.Ok(body));

        Result<CustomDataSourceTestFetchDto> result = await sut.TestFetchAsync(Channel, id);

        result.IsSuccess.Should().BeTrue();
        result.Value.RawJson.Should().Be(body);
        result
            .Value.KeyPaths.Should()
            .BeEquivalentTo("$.bpm", "$.meta.device", "$.meta.tags[0]", "$.meta.tags[1]");
    }

    [Fact]
    public async Task An_egress_gate_rejection_surfaces_as_a_failure_not_a_fetch()
    {
        (CustomDataSourceService sut, AuthDbContext db, ICustomDataEgressFetcher fetcher) = Build();
        Guid id = await SeedSourceAsync(db);

        fetcher
            .FetchAsync(
                Channel,
                "https://api.example.com/heart",
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                CustomDataEgressFetchResult.Fail(
                    CustomDataEgressFetchOutcome.NotAllowlisted,
                    "The target host 'api.example.com' is not in an enabled egress allowlist."
                )
            );

        Result<CustomDataSourceTestFetchDto> result = await sut.TestFetchAsync(Channel, id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("egress allowlist");
    }

    [Fact]
    public async Task A_missing_source_fails_without_calling_the_fetcher()
    {
        (CustomDataSourceService sut, AuthDbContext db, ICustomDataEgressFetcher fetcher) = Build();
        _ = db; // no source seeded

        Result<CustomDataSourceTestFetchDto> result = await sut.TestFetchAsync(
            Channel,
            Guid.NewGuid()
        );

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("NOT_FOUND");
        await fetcher
            .DidNotReceive()
            .FetchAsync(
                Arg.Any<Guid>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }
}
