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

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Pipeline;
using NomNomzBot.Application.Contracts.Platform;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Stream.Pipeline;
using NomNomzBot.Infrastructure.Stream.PipelineActions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Stream.PipelineActions;

/// <summary>
/// Proves the set_stream_info action: the exact update the platform receives (templated title, category
/// name, tag list), that an empty value means "leave unchanged" (null member, never an empty string or list
/// sent through), that an all-empty step fails without a call, that a missing scope and a platform refusal
/// come back as an action failure with the platform's own words, that more tags than the platform allows
/// fail before any call, and that the catalogue entry holds the three fields with their kinds. The category
/// picker's option provider is proven too: it searches the platform catalogue and saves the category name.
/// </summary>
public sealed class SetStreamInfoActionTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000b401");

    private sealed class RecordingPlatform : IPlatformChannelApi
    {
        public List<PlatformStreamInfoUpdate> Calls { get; } = [];

        public Result<PlatformStreamInfoApplied> Reply { get; set; } =
            Result.Success(new PlatformStreamInfoApplied("t", "c", null));

        public Task<Result<PlatformStreamInfoApplied>> UpdateStreamInfoAsync(
            Guid broadcasterId,
            PlatformStreamInfoUpdate update,
            CancellationToken cancellationToken = default
        )
        {
            Calls.Add(update);
            return Task.FromResult(Reply);
        }
    }

    private static TemplateResolver Resolver()
    {
        IServiceScopeFactory scopeFactory = Substitute.For<IServiceScopeFactory>();
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Arg.Any<Guid>()).Returns((ChannelContext?)null);
        return new(
            scopeFactory,
            registry,
            NullLogger<TemplateResolver>.Instance,
            TimeProvider.System
        );
    }

    private static PipelineExecutionContext Ctx()
    {
        PipelineExecutionContext ctx = new()
        {
            BroadcasterId = Channel,
            TriggeredByUserId = "tw-1",
            TriggeredByDisplayName = "Viewer",
            MessageId = "m1",
            RawMessage = "!title world",
        };
        ctx.Variables["args"] = "world";
        return ctx;
    }

    private static ActionDefinition Step(
        string? title = null,
        string? category = null,
        string[]? tags = null
    )
    {
        Dictionary<string, JsonElement> parameters = [];
        if (title is not null)
            parameters["title"] = JsonSerializer.SerializeToElement(title);
        if (category is not null)
            parameters["category"] = JsonSerializer.SerializeToElement(category);
        if (tags is not null)
            parameters["tags"] = JsonSerializer.SerializeToElement(tags);
        return new() { Type = "set_stream_info", Parameters = parameters };
    }

    [Fact]
    public async Task Title_category_and_tags_reach_the_platform_in_one_call_with_the_title_resolved()
    {
        RecordingPlatform platform = new();
        SetStreamInfoAction action = new(platform, Resolver());

        ActionResult result = await action.ExecuteAsync(
            Ctx(),
            Step("Hello {args}", "Just Chatting", ["a", "b"])
        );

        result.Succeeded.Should().BeTrue();
        platform.Calls.Should().ContainSingle();
        PlatformStreamInfoUpdate update = platform.Calls[0];
        update.Title.Should().Be("Hello world");
        update.CategoryName.Should().Be("Just Chatting");
        update.Tags.Should().Equal("a", "b");
        update.ContentLabels.Should().BeNull();
        update.IsBrandedContent.Should().BeNull();
    }

    [Fact]
    public async Task An_empty_title_category_and_tag_list_leave_those_members_unchanged()
    {
        RecordingPlatform platform = new();
        SetStreamInfoAction action = new(platform, Resolver());

        ActionResult result = await action.ExecuteAsync(
            Ctx(),
            Step(title: "", category: "  ", tags: ["", " "])
        );

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("title").And.Contain("category").And.Contain("tags");
        platform.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_step_with_no_values_at_all_fails_with_a_clear_text_and_makes_no_call()
    {
        RecordingPlatform platform = new();
        SetStreamInfoAction action = new(platform, Resolver());

        ActionResult result = await action.ExecuteAsync(Ctx(), Step());

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("set_stream_info needs at least one");
        platform.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_title_alone_leaves_the_category_and_tags_null()
    {
        RecordingPlatform platform = new();
        SetStreamInfoAction action = new(platform, Resolver());

        ActionResult result = await action.ExecuteAsync(Ctx(), Step(title: "Only a title"));

        result.Succeeded.Should().BeTrue();
        PlatformStreamInfoUpdate update = platform.Calls.Should().ContainSingle().Subject;
        update.Title.Should().Be("Only a title");
        update.CategoryName.Should().BeNull();
        update.Tags.Should().BeNull();
    }

    [Fact]
    public async Task A_title_that_resolves_to_nothing_is_left_unchanged()
    {
        RecordingPlatform platform = new();
        SetStreamInfoAction action = new(platform, Resolver());
        PipelineExecutionContext ctx = Ctx();
        ctx.Variables["args"] = "";

        ActionResult result = await action.ExecuteAsync(ctx, Step("{args}", "Chess"));

        result.Succeeded.Should().BeTrue();
        PlatformStreamInfoUpdate update = platform.Calls.Should().ContainSingle().Subject;
        update.Title.Should().BeNull();
        update.CategoryName.Should().Be("Chess");
    }

    [Fact]
    public async Task A_missing_scope_is_an_action_failure_that_names_the_scope()
    {
        RecordingPlatform platform = new()
        {
            Reply = Result.Failure<PlatformStreamInfoApplied>(
                "Missing required scope 'channel:manage:broadcast'",
                TwitchErrorCodes.MissingScope
            ),
        };
        SetStreamInfoAction action = new(platform, Resolver());

        ActionResult result = await action.ExecuteAsync(Ctx(), Step(title: "New title"));

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("channel:manage:broadcast").And.Contain("re-grant");
        platform.Calls.Should().ContainSingle();
    }

    [Fact]
    public async Task A_platform_refusal_carries_the_platforms_own_message()
    {
        RecordingPlatform platform = new()
        {
            Reply = Result.Failure<PlatformStreamInfoApplied>(
                "Title exceeds the 140 character limit.",
                TwitchErrorCodes.TwitchError
            ),
        };
        SetStreamInfoAction action = new(platform, Resolver());

        ActionResult result = await action.ExecuteAsync(Ctx(), Step(title: "Too long"));

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Title exceeds the 140 character limit.");
    }

    [Fact]
    public async Task More_tags_than_the_platform_allows_fail_before_any_call_and_name_the_limit()
    {
        RecordingPlatform platform = new();
        SetStreamInfoAction action = new(platform, Resolver());
        string[] tags = [.. Enumerable.Range(1, 11).Select(n => $"tag{n}")];

        ActionResult result = await action.ExecuteAsync(Ctx(), Step(title: "t", tags: tags));

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("10");
        platform.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_tag_longer_than_the_platform_allows_fails_before_any_call_and_names_the_limit()
    {
        RecordingPlatform platform = new();
        SetStreamInfoAction action = new(platform, Resolver());

        ActionResult result = await action.ExecuteAsync(
            Ctx(),
            Step(title: "t", tags: [new string('x', 26)])
        );

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("25");
        platform.Calls.Should().BeEmpty();
    }

    [Fact]
    public void The_catalogue_entry_holds_the_title_category_and_tags_fields_with_their_kinds()
    {
        SetStreamInfoAction action = new(new RecordingPlatform(), Resolver());

        action.ActionType.Should().Be("set_stream_info");
        action
            .Fields.Select(f => (f.Name, f.Kind.ToWireName(), f.Templated, f.Repeatable))
            .Should()
            .Equal(
                ("title", "text", true, false),
                ("category", "stream_category", false, false),
                ("tags", "text", false, true)
            );
        action.Fields.Should().OnlyContain(f => f.Description != null);
        PipelineActionFieldKind.StreamCategory.IsResourcePicker().Should().BeTrue();
    }

    [Fact]
    public void The_tags_field_tells_the_step_form_the_platform_limit_of_ten_and_no_other_field_has_a_limit()
    {
        SetStreamInfoAction action = new(new RecordingPlatform(), Resolver());

        action
            .Fields.Select(f => (f.Name, f.MaxItems))
            .Should()
            .Equal(("title", null), ("category", null), ("tags", 10));
    }

    [Fact]
    public async Task The_category_picker_lists_the_platform_search_with_the_name_as_the_saved_value()
    {
        ITwitchSearchApi search = Substitute.For<ITwitchSearchApi>();
        search
            .SearchCategoriesAsync(
                "chat",
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchSearchCategory>(
                        [
                            new("509658", "Just Chatting", "https://img/jc.jpg"),
                            new("1", "Chat Heroes", "https://img/ch.jpg"),
                        ],
                        null,
                        2
                    )
                )
            );
        StreamCategoryOptionProvider provider = new(search);

        Result<PipelineOptionListResult> result = await provider.GetOptionsAsync(
            Channel,
            new("chat", null, new())
        );

        provider.Kind.Should().Be(PipelineActionFieldKind.StreamCategory);
        result.IsSuccess.Should().BeTrue();
        result.Value.SourceAvailable.Should().BeTrue();
        result
            .Value.Items.Select(o => (o.Value, o.Label, o.ImageUrl))
            .Should()
            .Equal(
                ("Just Chatting", "Just Chatting", "https://img/jc.jpg"),
                ("Chat Heroes", "Chat Heroes", "https://img/ch.jpg")
            );
    }

    [Fact]
    public async Task The_category_picker_answers_a_saved_value_with_the_exact_match_only()
    {
        ITwitchSearchApi search = Substitute.For<ITwitchSearchApi>();
        search
            .SearchCategoriesAsync(
                "Chess",
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchSearchCategory>(
                        [new("2", "Chess Rush", "u2"), new("3", "Chess", "u3")],
                        null,
                        2
                    )
                )
            );
        StreamCategoryOptionProvider provider = new(search);

        Result<PipelineOptionListResult> result = await provider.GetOptionsAsync(
            Channel,
            new(null, "Chess", new())
        );

        result.Value.Items.Should().ContainSingle().Which.Value.Should().Be("Chess");
    }

    [Fact]
    public async Task The_category_picker_reports_an_unreadable_source_instead_of_an_empty_list()
    {
        ITwitchSearchApi search = Substitute.For<ITwitchSearchApi>();
        search
            .SearchCategoriesAsync(
                Arg.Any<string>(),
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Failure<TwitchPage<TwitchSearchCategory>>(
                    "Twitch is down",
                    TwitchErrorCodes.Transport
                )
            );
        StreamCategoryOptionProvider provider = new(search);

        Result<PipelineOptionListResult> result = await provider.GetOptionsAsync(
            Channel,
            new("chess", null, new())
        );

        result.Value.SourceAvailable.Should().BeFalse();
        result.Value.UnavailableReason.Should().Contain("Twitch is down");
    }
}
