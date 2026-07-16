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
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// Proves the chat-trigger CRUD honesty checks: a trigger must be able to DO something (template or
/// pipeline) and a regex must compile at write time; every successful write refreshes the hot-path
/// cache via the registry (so the change is live without a restart); cooldown/floor inputs are clamped.
/// </summary>
public sealed class ChatTriggerServiceTests
{
    private static readonly Guid Tenant = Guid.Parse("019f6e00-6666-7000-8000-000000000001");

    private static (ChatTriggerService Service, AuthDbContext Db, IChannelRegistry Registry) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        return (new ChatTriggerService(db, registry), db, registry);
    }

    [Fact]
    public async Task Create_persists_the_trigger_and_refreshes_the_hot_path_cache()
    {
        (ChatTriggerService service, AuthDbContext db, IChannelRegistry registry) = Build();

        Result<ChatTriggerDto> created = await service.CreateAsync(
            Tenant.ToString(),
            new CreateChatTriggerRequest { Pattern = "good bot", Response = "thanks {user}!" }
        );

        created.IsSuccess.Should().BeTrue();
        created.Value.MatchType.Should().Be("contains");
        created.Value.CooldownSeconds.Should().Be(30, "the spam-guard default");
        (await db.ChatTriggers.CountAsync()).Should().Be(1);
        await registry
            .Received(1)
            .InvalidateChatTriggersAsync(Tenant, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_trigger_with_nothing_to_do_is_rejected()
    {
        (ChatTriggerService service, _, IChannelRegistry registry) = Build();

        Result<ChatTriggerDto> created = await service.CreateAsync(
            Tenant.ToString(),
            new CreateChatTriggerRequest { Pattern = "hello" } // no response, no pipeline
        );

        created.IsFailure.Should().BeTrue();
        created.ErrorCode.Should().Be("VALIDATION_FAILED");
        await registry
            .DidNotReceiveWithAnyArgs()
            .InvalidateChatTriggersAsync(default, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_uncompilable_regex_is_rejected_at_write_time()
    {
        (ChatTriggerService service, _, _) = Build();

        Result<ChatTriggerDto> created = await service.CreateAsync(
            Tenant.ToString(),
            new CreateChatTriggerRequest
            {
                Pattern = "([unclosed",
                MatchType = "regex",
                Response = "hi",
            }
        );

        created.IsFailure.Should().BeTrue("an invalid regex would silently never fire");
        created.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task Update_is_partial_and_revalidates_the_combined_state()
    {
        (ChatTriggerService service, _, IChannelRegistry registry) = Build();
        Guid id = (
            await service.CreateAsync(
                Tenant.ToString(),
                new CreateChatTriggerRequest { Pattern = "hello", Response = "hi!" }
            )
        )
            .Value
            .Id;
        registry.ClearReceivedCalls();

        Result<ChatTriggerDto> updated = await service.UpdateAsync(
            Tenant.ToString(),
            id,
            new UpdateChatTriggerRequest { CooldownSeconds = 999_999, IsEnabled = false }
        );

        updated.Value.Pattern.Should().Be("hello", "absent fields stay unchanged");
        updated.Value.CooldownSeconds.Should().Be(86_400, "clamped to the 24h ceiling");
        updated.Value.IsEnabled.Should().BeFalse();
        await registry
            .Received(1)
            .InvalidateChatTriggersAsync(Tenant, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_removes_the_trigger_and_refreshes_the_cache()
    {
        (ChatTriggerService service, AuthDbContext db, IChannelRegistry registry) = Build();
        Guid id = (
            await service.CreateAsync(
                Tenant.ToString(),
                new CreateChatTriggerRequest { Pattern = "bye", Response = "cya" }
            )
        )
            .Value
            .Id;
        registry.ClearReceivedCalls();

        Result deleted = await service.DeleteAsync(Tenant.ToString(), id);

        deleted.IsSuccess.Should().BeTrue();
        (await db.ChatTriggers.IgnoreQueryFilters().CountAsync(t => t.DeletedAt == null))
            .Should()
            .Be(0);
        await registry
            .Received(1)
            .InvalidateChatTriggersAsync(Tenant, Arg.Any<CancellationToken>());
    }
}
