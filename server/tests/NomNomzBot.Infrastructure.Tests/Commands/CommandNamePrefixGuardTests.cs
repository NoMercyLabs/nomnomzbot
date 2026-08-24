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
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.RateLimiting;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Chat.EventHandlers;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// Pins the fix for a data-integrity defect confirmed on the live database: a user typed the channel's own
/// command prefix into the Name field of a custom command (Commands row
/// <c>01a0354e-88b2-701b-8f23-4288a568ece6</c>, <c>Name='!so'</c>, <c>PrefixMode=Default</c>,
/// <c>IsEnabled=true</c>). With PrefixMode=Default the dispatcher builds the trigger as
/// <c>channelPrefix + Name</c> (<see cref="ChatMessageHandler"/>'s private
/// <c>ResolveAuthoredCommand</c>), so a name that already carries its own effective prefix can never match —
/// the command is permanently dead and, since unmatched chat is silent by design, the author never learns why.
/// These tests drive <see cref="CommandService.CreateAsync"/>/<see cref="CommandService.UpdateAsync"/> for the
/// persistence-boundary guard, and then feed the ACTUAL persisted row's fields into a real
/// <see cref="ChatMessageHandler"/> dispatch — not a string comparison of the Name field — to prove the saved
/// command really does fire from the chat text a user would type.
/// </summary>
public sealed class CommandNamePrefixGuardTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000ec001");

    private static (CommandService Sut, CommandsTestDbContext Db) Build()
    {
        CommandsTestDbContext db = CommandsTestDbContext.New();
        CommandService sut = new(
            db,
            Substitute.For<IPipelineEngine>(),
            Substitute.For<IChannelRegistry>(),
            new RecordingEventBus(),
            Billing.TestTiers.Unlimited()
        );
        return (sut, db);
    }

    // ─── S1: the exact live-database shape is fixed at the source ────────────

    [Fact]
    public async Task Create_WithNameEqualToThePrefixPlusTheIntendedCommand_StripsThePrefixAndDispatchesForReal()
    {
        (CommandService sut, CommandsTestDbContext db) = Build();

        Result<CommandDto> created = await sut.CreateAsync(
            Channel.ToString(),
            new()
            {
                Name = "!so", // exactly the live defective shape — user typed the prefix into the name
                TemplateResponse = "shouting them out!",
                PrefixMode = "Default",
            }
        );

        created.IsSuccess.Should().BeTrue(created.ErrorMessage);
        // The response makes the correction visible immediately — the caller sees "so" come back, not "!so".
        created.Value.Name.Should().Be("so");

        NomNomzBot.Domain.Commands.Entities.Command persisted = await db.Commands.SingleAsync();
        persisted.Name.Should().Be("so");
        persisted.NameNormalized.Should().Be("so");

        // Now prove the SAVED ROW actually dispatches — drive the real trigger-building logic
        // (ChatMessageHandler.ResolveAuthoredCommand) with the persisted row's own fields, not a
        // hand-typed stand-in, against the exact chat text a viewer would type.
        IChatProvider chat = await DispatchAsync(persisted, "!so stoney_eagle");

        await chat.Received(1)
            .SendReplyAsync(
                Channel,
                Arg.Any<string>(),
                "shouting them out!",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Create_WithACustomPrefixEqualToTheNamesLeadingCharacter_StripsThatPrefixToo()
    {
        (CommandService sut, CommandsTestDbContext db) = Build();

        Result<CommandDto> created = await sut.CreateAsync(
            Channel.ToString(),
            new()
            {
                Name = "$deal",
                TemplateResponse = "dealt!",
                PrefixMode = "Custom",
                CustomPrefix = "$",
            }
        );

        created.IsSuccess.Should().BeTrue(created.ErrorMessage);
        created.Value.Name.Should().Be("deal");

        NomNomzBot.Domain.Commands.Entities.Command persisted = await db.Commands.SingleAsync();
        IChatProvider chat = await DispatchAsync(persisted, "$deal");

        await chat.Received(1)
            .SendReplyAsync(Channel, Arg.Any<string>(), "dealt!", Arg.Any<CancellationToken>());
    }

    // ─── S2: rejections ────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_WithNameThatIsOnlyThePrefix_IsRejectedAndPersistsNothing()
    {
        (CommandService sut, CommandsTestDbContext db) = Build();

        Result<CommandDto> result = await sut.CreateAsync(
            Channel.ToString(),
            new()
            {
                Name = "!",
                TemplateResponse = "hi",
                PrefixMode = "Default",
            }
        );

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        (await db.Commands.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Create_WithAnEmptyName_IsRejected()
    {
        (CommandService sut, CommandsTestDbContext db) = Build();

        Result<CommandDto> result = await sut.CreateAsync(
            Channel.ToString(),
            new() { Name = "   ", TemplateResponse = "hi" }
        );

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        (await db.Commands.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Create_WithANameContainingWhitespace_IsRejected()
    {
        (CommandService sut, CommandsTestDbContext db) = Build();

        Result<CommandDto> result = await sut.CreateAsync(
            Channel.ToString(),
            new() { Name = "so cool", TemplateResponse = "hi" }
        );

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        (await db.Commands.CountAsync()).Should().Be(0);
    }

    // ─── S3: an already-correct command is unaffected ─────────────────────────

    [Fact]
    public async Task Create_WithACorrectlyNamedCommand_IsUnaffected()
    {
        (CommandService sut, CommandsTestDbContext db) = Build();

        Result<CommandDto> created = await sut.CreateAsync(
            Channel.ToString(),
            new()
            {
                Name = "so",
                TemplateResponse = "shoutout!",
                PrefixMode = "Default",
            }
        );

        created.IsSuccess.Should().BeTrue(created.ErrorMessage);
        created.Value.Name.Should().Be("so");
        (await db.Commands.SingleAsync()).Name.Should().Be("so");
    }

    // ─── S4: a template-tier command with no response is a second silent failure ──

    [Fact]
    public async Task Create_TemplateTierWithNoResponseAtAll_IsRejected()
    {
        (CommandService sut, CommandsTestDbContext db) = Build();

        Result<CommandDto> result = await sut.CreateAsync(
            Channel.ToString(),
            new() { Name = "so", Tier = "template" } // TemplateResponse null, TemplateResponses null
        );

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        (await db.Commands.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Update_ClearingATemplateCommandsOnlyResponse_IsRejectedAndTheOldResponseSurvives()
    {
        (CommandService sut, CommandsTestDbContext db) = Build();
        await sut.CreateAsync(
            Channel.ToString(),
            new() { Name = "so", TemplateResponse = "shoutout!" }
        );

        Result<CommandDto> updated = await sut.UpdateAsync(
            Channel.ToString(),
            "so",
            new() { TemplateResponse = "", TemplateResponses = [] }
        );

        updated.IsSuccess.Should().BeFalse();
        updated.ErrorCode.Should().Be("VALIDATION_FAILED");
        (await db.Commands.SingleAsync()).TemplateResponse.Should().Be("shoutout!");
    }

    // ─── harness: real ChatMessageHandler dispatch over a persisted Command row ──

    /// <summary>
    /// Builds a real <see cref="ChatMessageHandler"/>, feeds it a <see cref="CachedCommand"/> copied verbatim
    /// from the PERSISTED <see cref="NomNomzBot.Domain.Commands.Entities.Command"/> row's own fields (the exact
    /// same copy <c>ChannelRegistry.LoadCommandsAsync</c> performs), and runs the given chat text through it.
    /// </summary>
    private static async Task<IChatProvider> DispatchAsync(
        NomNomzBot.Domain.Commands.Entities.Command persisted,
        string chatText
    )
    {
        ChannelContext ctx = new()
        {
            BroadcasterId = Channel,
            TwitchChannelId = "tw-777",
            ChannelName = "stoney_eagle",
        };
        string[] templateResponses = persisted.TemplateResponse is { } single
            ? [single]
            : [.. persisted.TemplateResponses ?? []];
        CachedCommand cached = new()
        {
            Name = persisted.Name,
            TemplateResponses = templateResponses,
            GlobalCooldown = 0,
            UserCooldown = 0,
            MinPermissionLevel = persisted.MinPermissionLevel,
            Tier = persisted.Tier,
            PrefixMode = persisted.PrefixMode,
            CustomPrefix = persisted.CustomPrefix,
            MatchMode = persisted.MatchMode,
        };
        ctx.Commands[cached.Name.TrimStart('!').ToLowerInvariant()] = cached;

        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Channel).Returns(ctx);

        IUserService users = Substitute.For<IUserService>();
        users
            .GetOrCreateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new UserDto(
                        Guid.CreateVersion7().ToString(),
                        "viewer",
                        "Viewer",
                        null,
                        null,
                        DateTime.UtcNow,
                        DateTime.UtcNow
                    )
                )
            );

        ServiceCollection services = new();
        services.AddSingleton(users);
        ServiceProvider provider = services.BuildServiceProvider();

        ITemplateResolver templates = Substitute.For<ITemplateResolver>();
        templates
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<Dictionary<string, string>>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(callInfo => callInfo.ArgAt<string>(0));

        IChatProvider chat = Substitute.For<IChatProvider>();

        ChatMessageHandler sut = new(
            registry,
            provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<ICooldownManager>(),
            chat,
            Substitute.For<IPipelineEngine>(),
            Substitute.For<IBuiltinCommandCatalog>(),
            templates,
            Substitute.For<IEventBus>(),
            new(),
            new FakeTimeProvider(DateTime.UtcNow),
            NullLogger<ChatMessageHandler>.Instance
        );

        await sut.HandleAsync(
            new()
            {
                BroadcasterId = Channel,
                MessageId = "msg-1",
                TwitchBroadcasterId = "tw-777",
                UserId = "tw-viewer-1",
                UserDisplayName = "Viewer",
                UserLogin = "viewer",
                Message = chatText,
                Fragments = [],
                Badges = [],
                IsSubscriber = false,
                IsVip = false,
                IsModerator = false,
                IsBroadcaster = false,
            },
            CancellationToken.None
        );

        return chat;
    }
}
