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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Consequences;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Webhooks;
using NomNomzBot.Application.CustomEvents.Services;
using NomNomzBot.Application.DTOs.Egress;
using NomNomzBot.Application.DTOs.Webhooks;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.CustomEvents.Entities;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Domain.Webhooks.Entities;
using NomNomzBot.Infrastructure.CustomEvents;
using NomNomzBot.Infrastructure.Egress;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Webhooks;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Egress;

/// <summary>
/// Proves the egress allowlist management: a stored row is exactly what the consumers (outbound webhooks) read,
/// a bad host is refused before anything is stored, and one channel can never see or change another's rows.
/// </summary>
public sealed class HttpEgressAllowlistServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000000e01");
    private static readonly Guid OtherChannel = Guid.Parse("0192a000-0000-7000-8000-000000000e02");
    private static readonly Guid Actor = Guid.Parse("0192a000-0000-7000-8000-000000000e03");
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private static (HttpEgressAllowlistService Sut, AuthDbContext Db) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        return (new HttpEgressAllowlistService(db, new FakeTimeProvider(Now)), db);
    }

    private static CreateHttpEgressAllowlistRequest Req(string host) => new() { Host = host };

    private static OutboundWebhookEndpointService BuildOutbound(AuthDbContext db)
    {
        ITokenProtector protector = Substitute.For<ITokenProtector>();
        protector
            .ProtectAsync(
                Arg.Any<string>(),
                Arg.Any<TokenProtectionContext>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ci => Task.FromResult($"sealed:{ci.ArgAt<string>(0)}"));
        ISubjectKeyService keys = Substitute.For<ISubjectKeyService>();
        keys.GetOrCreateSubjectKeyAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(Guid.Parse("0192a000-0000-7000-8000-0000000000cc")));
        return new OutboundWebhookEndpointService(
            db,
            protector,
            keys,
            new FakeTimeProvider(Now),
            new RecordingEventBus(),
            new TemplateHelperValidator(),
            Substitute.For<IOutboundWebhookDispatcher>()
        );
    }

    [Fact]
    public async Task CreateAsync_stores_an_enabled_lowercase_row_approved_by_the_actor()
    {
        (HttpEgressAllowlistService sut, AuthDbContext db) = Build();

        Result<HttpEgressAllowlistDto> result = await sut.CreateAsync(
            Channel,
            Actor,
            Req("  Hooks.Example.COM ")
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        HttpEgressAllowlist row = await db.HttpEgressAllowlists.SingleAsync();
        row.BroadcasterId.Should().Be(Channel);
        row.Fqdn.Should().Be("hooks.example.com");
        row.IsEnabled.Should().BeTrue();
        row.ApprovedByUserId.Should().Be(Actor);
        result.Value.Id.Should().Be(row.Id);
        result.Value.Host.Should().Be("hooks.example.com");
        result.Value.IsEnabled.Should().BeTrue();
        result.Value.ApprovedByUserId.Should().Be(Actor);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://hooks.example.com")]
    [InlineData("hooks.example.com/path")]
    [InlineData("hooks.example.com:8443")]
    [InlineData("hooks.example.com?x=1")]
    [InlineData("user@hooks.example.com")]
    [InlineData("*.example.com")]
    [InlineData("exa*mple.com")]
    [InlineData("hooks example.com")]
    [InlineData("localhost")]
    [InlineData("app.localhost")]
    [InlineData("printer.local")]
    [InlineData("db.internal")]
    [InlineData("intranet")]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.5")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("[::1]")]
    [InlineData("::1")]
    [InlineData("2130706433")]
    [InlineData("-bad.example.com")]
    [InlineData("bad-.example.com")]
    [InlineData("bad..example.com")]
    [InlineData("example.com.")]
    public async Task CreateAsync_refuses_an_unsafe_host_and_stores_nothing(string host)
    {
        (HttpEgressAllowlistService sut, AuthDbContext db) = Build();

        Result<HttpEgressAllowlistDto> result = await sut.CreateAsync(Channel, Actor, Req(host));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        (await db.HttpEgressAllowlists.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CreateAsync_refuses_a_duplicate_for_the_same_channel_but_allows_another_channel()
    {
        (HttpEgressAllowlistService sut, AuthDbContext db) = Build();
        (await sut.CreateAsync(Channel, Actor, Req("hooks.example.com")))
            .IsSuccess.Should()
            .BeTrue();

        Result<HttpEgressAllowlistDto> duplicate = await sut.CreateAsync(
            Channel,
            Actor,
            Req("HOOKS.example.com")
        );
        Result<HttpEgressAllowlistDto> otherChannel = await sut.CreateAsync(
            OtherChannel,
            Actor,
            Req("hooks.example.com")
        );

        duplicate.IsFailure.Should().BeTrue();
        duplicate.ErrorCode.Should().Be("ALREADY_EXISTS");
        otherChannel.IsSuccess.Should().BeTrue(otherChannel.ErrorMessage);
        (await db.HttpEgressAllowlists.CountAsync(a => a.BroadcasterId == Channel)).Should().Be(1);
        (await db.HttpEgressAllowlists.CountAsync(a => a.BroadcasterId == OtherChannel))
            .Should()
            .Be(1);
    }

    [Fact]
    public async Task ListAsync_returns_only_the_callers_non_deleted_rows()
    {
        (HttpEgressAllowlistService sut, AuthDbContext db) = Build();
        await sut.CreateAsync(Channel, Actor, Req("a.example.com"));
        await sut.CreateAsync(Channel, Actor, Req("b.example.com"));
        await sut.CreateAsync(OtherChannel, Actor, Req("c.example.com"));
        db.HttpEgressAllowlists.Add(
            new HttpEgressAllowlist
            {
                BroadcasterId = Channel,
                Fqdn = "gone.example.com",
                IsEnabled = true,
                DeletedAt = Now.UtcDateTime,
            }
        );
        await db.SaveChangesAsync();

        Result<PagedList<HttpEgressAllowlistDto>> result = await sut.ListAsync(
            Channel,
            new PaginationParams()
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(2);
        result
            .Value.Items.Select(i => i.Host)
            .Should()
            .BeEquivalentTo("a.example.com", "b.example.com");
    }

    [Fact]
    public async Task SetEnabledAsync_persists_the_switch()
    {
        (HttpEgressAllowlistService sut, AuthDbContext db) = Build();
        Guid id = (await sut.CreateAsync(Channel, Actor, Req("hooks.example.com"))).Value.Id;

        Result<HttpEgressAllowlistDto> off = await sut.SetEnabledAsync(Channel, id, false);

        off.IsSuccess.Should().BeTrue(off.ErrorMessage);
        off.Value.IsEnabled.Should().BeFalse();
        (await db.HttpEgressAllowlists.SingleAsync()).IsEnabled.Should().BeFalse();

        Result<HttpEgressAllowlistDto> on = await sut.SetEnabledAsync(Channel, id, true);

        on.Value.IsEnabled.Should().BeTrue();
        (await db.HttpEgressAllowlists.SingleAsync()).IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task SetEnabledAsync_refuses_another_channels_row_and_changes_nothing()
    {
        (HttpEgressAllowlistService sut, AuthDbContext db) = Build();
        Guid id = (await sut.CreateAsync(Channel, Actor, Req("hooks.example.com"))).Value.Id;

        Result<HttpEgressAllowlistDto> result = await sut.SetEnabledAsync(OtherChannel, id, false);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_FOUND");
        (await db.HttpEgressAllowlists.SingleAsync()).IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task SetEnabledAsync_reports_not_found_for_an_unknown_id()
    {
        (HttpEgressAllowlistService sut, _) = Build();

        Result<HttpEgressAllowlistDto> result = await sut.SetEnabledAsync(
            Channel,
            Guid.NewGuid(),
            false
        );

        result.ErrorCode.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Disabling_a_host_makes_the_same_outbound_webhook_save_fail()
    {
        (HttpEgressAllowlistService sut, AuthDbContext db) = Build();
        OutboundWebhookEndpointService outbound = BuildOutbound(db);
        Guid id = (await sut.CreateAsync(Channel, Actor, Req("hooks.example.com"))).Value.Id;
        CreateOutboundWebhookRequest request = new()
        {
            Name = "endpoint",
            Fqdn = "hooks.example.com",
            SubscribedEventTypes = ["*"],
        };

        (await outbound.CreateAsync(Channel, Actor, request)).IsSuccess.Should().BeTrue();

        await sut.SetEnabledAsync(Channel, id, false);
        Result<OutboundWebhookEndpointCreatedDto> refused = await outbound.CreateAsync(
            Channel,
            Actor,
            request
        );

        refused.IsFailure.Should().BeTrue();
        refused.ErrorCode.Should().Be("EGRESS_NOT_ALLOWED");
        (await db.OutboundWebhookEndpoints.CountAsync()).Should().Be(1);
    }

    private static async Task<OutboundWebhookEndpoint> SeedOutboundAsync(
        AuthDbContext db,
        Guid channel,
        string name,
        string fqdn,
        Guid? allowlistId,
        bool deleted = false
    )
    {
        OutboundWebhookEndpoint endpoint = new()
        {
            BroadcasterId = channel,
            Name = name,
            Fqdn = fqdn,
            HttpEgressAllowlistId = allowlistId,
            SigningSecretEnvelope = "sealed",
            IsEnabled = true,
            DeletedAt = deleted ? Now.UtcDateTime : null,
        };
        db.OutboundWebhookEndpoints.Add(endpoint);
        await db.SaveChangesAsync();
        return endpoint;
    }

    private static async Task SeedSourceAsync(
        AuthDbContext db,
        Guid channel,
        string displayName,
        string? url,
        bool deleted = false
    )
    {
        db.CustomDataSources.Add(
            new CustomDataSource
            {
                BroadcasterId = channel,
                Name = displayName.Replace(' ', '_').ToLowerInvariant(),
                DisplayName = displayName,
                SourceKind = "poll",
                EndpointUrl = url,
                DeletedAt = deleted ? Now.UtcDateTime : null,
            }
        );
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetDeleteBlastRadiusAsync_counts_the_outbound_endpoints_and_sources_on_the_host()
    {
        (HttpEgressAllowlistService sut, AuthDbContext db) = Build();
        Guid id = (await sut.CreateAsync(Channel, Actor, Req("hooks.example.com"))).Value.Id;
        Guid otherHostId = (await sut.CreateAsync(Channel, Actor, Req("other.example.com")))
            .Value
            .Id;
        Guid foreignId = (await sut.CreateAsync(OtherChannel, Actor, Req("hooks.example.com")))
            .Value
            .Id;
        await SeedOutboundAsync(db, Channel, "by-link", "hooks.example.com", id);
        await SeedOutboundAsync(db, Channel, "by-name", "hooks.example.com", null);
        await SeedOutboundAsync(db, Channel, "elsewhere", "other.example.com", otherHostId);
        await SeedOutboundAsync(db, Channel, "removed", "hooks.example.com", id, deleted: true);
        await SeedOutboundAsync(db, OtherChannel, "foreign", "hooks.example.com", foreignId);
        await SeedSourceAsync(db, Channel, "Heart Rate", "https://hooks.example.com:8443/hr?x=1");
        await SeedSourceAsync(db, Channel, "Weather", "https://other.example.com/w");
        await SeedSourceAsync(db, Channel, "Push Only", null);
        await SeedSourceAsync(db, Channel, "Gone", "https://hooks.example.com/g", deleted: true);
        await SeedSourceAsync(db, OtherChannel, "Foreign Feed", "https://hooks.example.com/f");

        Result<BlastRadiusDto> result = await sut.GetDeleteBlastRadiusAsync(Channel, id);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.IsMinimum.Should().BeFalse();
        result.Value.TotalReferences.Should().Be(3);
        result.Value.Categories.Should().HaveCount(2);
        BlastRadiusCategoryDto outbound = result.Value.Categories.Single(c =>
            c.CategoryKey == BlastRadiusCategoryKeys.OutboundWebhookEndpoints
        );
        outbound.Count.Should().Be(2);
        outbound.Sample.Should().Equal("by-link", "by-name");
        BlastRadiusCategoryDto sources = result.Value.Categories.Single(c =>
            c.CategoryKey == BlastRadiusCategoryKeys.CustomDataSources
        );
        sources.Count.Should().Be(1);
        sources.Sample.Should().Equal("Heart Rate");
        (await db.HttpEgressAllowlists.CountAsync(a => a.DeletedAt != null))
            .Should()
            .Be(0, "the preview never changes anything");
    }

    [Fact]
    public async Task GetDeleteBlastRadiusAsync_reports_an_explicit_zero_for_an_unused_host()
    {
        (HttpEgressAllowlistService sut, _) = Build();
        Guid id = (await sut.CreateAsync(Channel, Actor, Req("hooks.example.com"))).Value.Id;

        Result<BlastRadiusDto> result = await sut.GetDeleteBlastRadiusAsync(Channel, id);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Categories.Should().BeEmpty();
        result.Value.TotalReferences.Should().Be(0);
    }

    [Fact]
    public async Task GetDeleteBlastRadiusAsync_fails_for_an_unknown_or_foreign_or_deleted_host()
    {
        (HttpEgressAllowlistService sut, _) = Build();
        Guid id = (await sut.CreateAsync(Channel, Actor, Req("hooks.example.com"))).Value.Id;
        Guid deletedId = (await sut.CreateAsync(Channel, Actor, Req("old.example.com"))).Value.Id;
        await sut.DeleteAsync(Channel, deletedId);

        (await sut.GetDeleteBlastRadiusAsync(Channel, Guid.NewGuid()))
            .ErrorCode.Should()
            .Be("NOT_FOUND");
        (await sut.GetDeleteBlastRadiusAsync(OtherChannel, id)).ErrorCode.Should().Be("NOT_FOUND");
        (await sut.GetDeleteBlastRadiusAsync(Channel, deletedId))
            .ErrorCode.Should()
            .Be("NOT_FOUND");
    }

    [Fact]
    public async Task DeleteAsync_soft_deletes_only_this_channels_row()
    {
        (HttpEgressAllowlistService sut, AuthDbContext db) = Build();
        Guid id = (await sut.CreateAsync(Channel, Actor, Req("hooks.example.com"))).Value.Id;
        await sut.CreateAsync(Channel, Actor, Req("keep.example.com"));
        await sut.CreateAsync(OtherChannel, Actor, Req("hooks.example.com"));

        Result result = await sut.DeleteAsync(Channel, id);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        HttpEgressAllowlist row = await db.HttpEgressAllowlists.SingleAsync(a => a.Id == id);
        row.DeletedAt.Should().Be(Now.UtcDateTime);
        (await db.HttpEgressAllowlists.CountAsync(a => a.DeletedAt == null)).Should().Be(2);
        (await db.HttpEgressAllowlists.SingleAsync(a => a.BroadcasterId == OtherChannel))
            .DeletedAt.Should()
            .BeNull();
        Result<PagedList<HttpEgressAllowlistDto>> list = await sut.ListAsync(
            Channel,
            new PaginationParams()
        );
        list.Value.Items.Select(i => i.Host).Should().Equal("keep.example.com");
    }

    [Fact]
    public async Task DeleteAsync_refuses_another_channels_row_an_unknown_id_and_a_second_delete()
    {
        (HttpEgressAllowlistService sut, AuthDbContext db) = Build();
        Guid id = (await sut.CreateAsync(Channel, Actor, Req("hooks.example.com"))).Value.Id;

        (await sut.DeleteAsync(OtherChannel, id)).ErrorCode.Should().Be("NOT_FOUND");
        (await sut.DeleteAsync(Channel, Guid.NewGuid())).ErrorCode.Should().Be("NOT_FOUND");
        (await db.HttpEgressAllowlists.SingleAsync()).DeletedAt.Should().BeNull();

        (await sut.DeleteAsync(Channel, id)).IsSuccess.Should().BeTrue();
        (await sut.DeleteAsync(Channel, id)).ErrorCode.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Deleting_a_host_makes_the_same_outbound_webhook_save_fail()
    {
        (HttpEgressAllowlistService sut, AuthDbContext db) = Build();
        OutboundWebhookEndpointService outbound = BuildOutbound(db);
        Guid id = (await sut.CreateAsync(Channel, Actor, Req("hooks.example.com"))).Value.Id;
        CreateOutboundWebhookRequest request = new()
        {
            Name = "endpoint",
            Fqdn = "hooks.example.com",
            SubscribedEventTypes = ["*"],
        };
        (await outbound.CreateAsync(Channel, Actor, request)).IsSuccess.Should().BeTrue();

        await sut.DeleteAsync(Channel, id);
        Result<OutboundWebhookEndpointCreatedDto> refused = await outbound.CreateAsync(
            Channel,
            Actor,
            request
        );

        refused.IsFailure.Should().BeTrue();
        refused.ErrorCode.Should().Be("EGRESS_NOT_ALLOWED");
        (await db.OutboundWebhookEndpoints.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Deleting_a_host_makes_the_same_custom_data_source_save_fail()
    {
        (HttpEgressAllowlistService sut, AuthDbContext db) = Build();
        CustomDataSourceService sources = new(
            db,
            Substitute.For<ITokenProtector>(),
            Substitute.For<ICustomDataIngestService>(),
            Substitute.For<ICustomDataEgressFetcher>(),
            [],
            Substitute.For<IInboundWebhookEndpointService>()
        );
        Guid id = (await sut.CreateAsync(Channel, Actor, Req("hooks.example.com"))).Value.Id;
        UpsertCustomDataSourceRequest request = new(
            Name: "sensor",
            DisplayName: "Sensor Feed",
            SourceKind: "poll",
            PresetKey: null,
            EndpointUrl: "https://hooks.example.com/data",
            AuthSecret: null,
            FieldMap: new Dictionary<string, string>(),
            PollIntervalSeconds: 30,
            IsEnabled: true
        );
        (await sources.CreateAsync(Channel, Actor, request)).IsSuccess.Should().BeTrue();

        await sut.DeleteAsync(Channel, id);
        Result<CustomDataSourceDto> refused = await sources.CreateAsync(
            Channel,
            Actor,
            request with
            {
                Name = "sensor2",
            }
        );

        refused.IsFailure.Should().BeTrue();
        refused.ErrorCode.Should().Be("EGRESS_NOT_ALLOWED");
        (await db.CustomDataSources.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task DeleteAsync_switches_off_the_outbound_endpoints_that_used_the_host()
    {
        (HttpEgressAllowlistService sut, AuthDbContext db) = Build();
        Guid id = (await sut.CreateAsync(Channel, Actor, Req("hooks.example.com"))).Value.Id;
        Guid otherId = (await sut.CreateAsync(Channel, Actor, Req("other.example.com"))).Value.Id;
        Guid foreignId = (await sut.CreateAsync(OtherChannel, Actor, Req("hooks.example.com")))
            .Value
            .Id;
        OutboundWebhookEndpoint byLink = await SeedOutboundAsync(
            db,
            Channel,
            "by-link",
            "hooks.example.com",
            id
        );
        OutboundWebhookEndpoint byName = await SeedOutboundAsync(
            db,
            Channel,
            "by-name",
            "hooks.example.com",
            null
        );
        OutboundWebhookEndpoint elsewhere = await SeedOutboundAsync(
            db,
            Channel,
            "elsewhere",
            "other.example.com",
            otherId
        );
        OutboundWebhookEndpoint foreign = await SeedOutboundAsync(
            db,
            OtherChannel,
            "foreign",
            "hooks.example.com",
            foreignId
        );

        await sut.DeleteAsync(Channel, id);

        foreach (Guid endpointId in new[] { byLink.Id, byName.Id })
        {
            OutboundWebhookEndpoint stored = await db.OutboundWebhookEndpoints.SingleAsync(e =>
                e.Id == endpointId
            );
            stored.IsEnabled.Should().BeFalse();
            stored.DisabledAt.Should().Be(Now.UtcDateTime);
            stored.DeletedAt.Should().BeNull("the endpoint stays so the owner can re-point it");
        }
        (await db.OutboundWebhookEndpoints.SingleAsync(e => e.Id == elsewhere.Id))
            .IsEnabled.Should()
            .BeTrue();
        (await db.OutboundWebhookEndpoints.SingleAsync(e => e.Id == foreign.Id))
            .IsEnabled.Should()
            .BeTrue();
    }

    [Fact]
    public async Task A_deleted_host_can_be_approved_again_as_a_new_row()
    {
        (HttpEgressAllowlistService sut, AuthDbContext db) = Build();
        Guid id = (await sut.CreateAsync(Channel, Actor, Req("hooks.example.com"))).Value.Id;
        await sut.DeleteAsync(Channel, id);

        Result<HttpEgressAllowlistDto> again = await sut.CreateAsync(
            Channel,
            Actor,
            Req("hooks.example.com")
        );

        again.IsSuccess.Should().BeTrue(again.ErrorMessage);
        again.Value.Id.Should().NotBe(id);
        (await db.HttpEgressAllowlists.CountAsync(a => a.DeletedAt == null)).Should().Be(1);
    }
}
