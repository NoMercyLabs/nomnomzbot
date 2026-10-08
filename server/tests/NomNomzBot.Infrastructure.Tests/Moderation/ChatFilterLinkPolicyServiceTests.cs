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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Domain.Moderation.ChatFilters;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Moderation.Enums;
using NomNomzBot.Infrastructure.Moderation;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// Proves a link-policy filter's policy is validated when it is saved: a malformed document or an invalid
/// domain comes back as a <c>VALIDATION_FAILED</c> failure naming the problem and nothing is persisted or
/// changed; a good policy is stored in its canonical form, read back typed, and reaches the dashboard tester.
/// </summary>
public sealed class ChatFilterLinkPolicyServiceTests
{
    private static readonly Guid Broadcaster = Guid.CreateVersion7();

    private static CreateChatFilterRequest NewLinkFilter(
        LinkPolicy? policy = null,
        string? policyJson = null
    ) =>
        new()
        {
            FilterType = ChatFilterType.LinkPolicy,
            Name = "links",
            Action = ChatFilterAction.Delete,
            LinkPolicy = policy,
            LinkPolicyJson = policyJson,
        };

    [Fact]
    public async Task CreateAsync_stores_a_typed_policy_in_canonical_form_and_returns_it_typed()
    {
        await using ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        ChatFilterService service = new(db);

        Result<ChatFilterDto> result = await service.CreateAsync(
            Broadcaster,
            NewLinkFilter(
                new LinkPolicy { AllowedDomains = [" Example.COM ", "example.com", "twitch.tv."] }
            )
        );

        result.IsSuccess.Should().BeTrue();
        result.Value!.LinkPolicy!.AllowedDomains.Should().Equal("example.com", "twitch.tv");
        result.Value.LinkPolicy.MatchBareDomains.Should().BeFalse();
        (await db.ChatFilters.SingleAsync())
            .LinkPolicyJson.Should()
            .Be("""{"allowedDomains":["example.com","twitch.tv"],"matchBareDomains":false}""");
    }

    [Fact]
    public async Task CreateAsync_accepts_the_older_json_field_and_exposes_it_typed()
    {
        await using ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        ChatFilterService service = new(db);

        Result<ChatFilterDto> result = await service.CreateAsync(
            Broadcaster,
            NewLinkFilter(
                policyJson: """{"allowedDomains":["example.com"],"matchBareDomains":true}"""
            )
        );

        result.IsSuccess.Should().BeTrue();
        result.Value!.LinkPolicy.Should().NotBeNull();
        result.Value.LinkPolicy!.AllowedDomains.Should().Equal("example.com");
        result.Value.LinkPolicy.MatchBareDomains.Should().BeTrue();
    }

    [Fact]
    public async Task CreateAsync_without_a_policy_gives_the_default_policy_where_every_link_trips()
    {
        await using ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        ChatFilterService service = new(db);

        Result<ChatFilterDto> result = await service.CreateAsync(Broadcaster, NewLinkFilter());

        result.IsSuccess.Should().BeTrue();
        result.Value!.LinkPolicy!.AllowedDomains.Should().BeEmpty();
        (await db.ChatFilters.SingleAsync()).LinkPolicyJson.Should().BeNull();
    }

    [Theory]
    [InlineData("""{"allowedDomains":["example.com"]""")]
    [InlineData("not json")]
    [InlineData("""{"allowedDomains":"example.com"}""")]
    [InlineData("""[]""")]
    public async Task CreateAsync_rejects_malformed_policy_json_and_persists_nothing(string json)
    {
        await using ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        ChatFilterService service = new(db);

        Result<ChatFilterDto> result = await service.CreateAsync(
            Broadcaster,
            NewLinkFilter(policyJson: json)
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        result.ErrorMessage.Should().Contain("not valid JSON");
        (await db.ChatFilters.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("example.com/path")]
    [InlineData("exa mple.com")]
    [InlineData("example.com:8080")]
    [InlineData("com")]
    [InlineData("")]
    [InlineData("192.168.0.1")]
    public async Task CreateAsync_rejects_an_invalid_domain_naming_it_and_persists_nothing(
        string domain
    )
    {
        await using ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        ChatFilterService service = new(db);

        Result<ChatFilterDto> result = await service.CreateAsync(
            Broadcaster,
            NewLinkFilter(new LinkPolicy { AllowedDomains = ["good.com", domain] })
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        result.ErrorMessage.Should().Contain($"\"{domain}\" is not a valid domain name");
        (await db.ChatFilters.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CreateAsync_rejects_an_invalid_domain_inside_the_older_json_field_too()
    {
        await using ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        ChatFilterService service = new(db);

        Result<ChatFilterDto> result = await service.CreateAsync(
            Broadcaster,
            NewLinkFilter(policyJson: """{"allowedDomains":["https://example.com"]}""")
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorMessage.Should().Contain("\"https://example.com\" is not a valid domain name");
        (await db.ChatFilters.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task UpdateAsync_replaces_the_policy_and_keeps_the_rest_of_the_filter()
    {
        await using ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        ChatFilterService service = new(db);
        Guid id = (
            await service.CreateAsync(
                Broadcaster,
                NewLinkFilter(new LinkPolicy { AllowedDomains = ["old.com"] })
            )
        )
            .Value!
            .Id;

        Result<ChatFilterDto> result = await service.UpdateAsync(
            Broadcaster,
            id,
            new() { LinkPolicy = new LinkPolicy { AllowedDomains = ["new.com"] } }
        );

        result.IsSuccess.Should().BeTrue();
        ChatFilter persisted = await db.ChatFilters.AsNoTracking().SingleAsync();
        LinkPolicy
            .FromStoredJson(persisted.LinkPolicyJson)
            .AllowedDomains.Should()
            .Equal("new.com");
        persisted.Name.Should().Be("links");
        persisted.Action.Should().Be(ChatFilterAction.Delete);
    }

    [Fact]
    public async Task UpdateAsync_rejects_an_invalid_policy_and_leaves_the_stored_one_untouched()
    {
        await using ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        ChatFilterService service = new(db);
        Guid id = (
            await service.CreateAsync(
                Broadcaster,
                NewLinkFilter(new LinkPolicy { AllowedDomains = ["old.com"] })
            )
        )
            .Value!
            .Id;
        string? before = (await db.ChatFilters.SingleAsync()).LinkPolicyJson;

        Result<ChatFilterDto> result = await service.UpdateAsync(
            Broadcaster,
            id,
            new() { Name = "renamed", LinkPolicyJson = """{"allowedDomains":["bad host"]}""" }
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        ChatFilter persisted = await db.ChatFilters.AsNoTracking().SingleAsync();
        persisted.LinkPolicyJson.Should().Be(before);
        persisted.Name.Should().Be("links");
    }

    [Fact]
    public async Task TestPattern_applies_the_given_policy_to_the_sample_message()
    {
        await using ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        ChatFilterService service = new(db);
        LinkPolicy policy = new() { AllowedDomains = ["example.com"] };

        bool allowed = service
            .TestPattern(Test("see https://shop.example.com/x", policy))
            .Value!.IsMatch;
        bool blocked = service.TestPattern(Test("see https://other.net/x", policy)).Value!.IsMatch;

        allowed.Should().BeFalse();
        blocked.Should().BeTrue();
    }

    [Fact]
    public async Task TestPattern_reports_an_invalid_policy_as_an_error_instead_of_a_match()
    {
        await using ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        ChatFilterService service = new(db);

        ChatFilterTestResult result = service
            .TestPattern(
                Test("https://x.net", new LinkPolicy { AllowedDomains = ["https://example.com"] })
            )
            .Value!;

        result.IsMatch.Should().BeFalse();
        result.CompileError.Should().Contain("is not a valid domain name");
    }

    private static TestChatFilterRequest Test(string sample, LinkPolicy policy) =>
        new()
        {
            FilterType = ChatFilterType.LinkPolicy,
            SampleMessage = sample,
            LinkPolicy = policy,
        };
}
