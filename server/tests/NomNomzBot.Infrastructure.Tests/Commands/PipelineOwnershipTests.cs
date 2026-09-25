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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// Proves the one check every pipeline-binding surface runs before it saves a request-supplied pipeline id
/// (<see cref="PipelineOwnership.EnsurePipelineInChannelAsync"/>): a known GUID belonging to another channel,
/// or a soft-deleted pipeline, must never bind and run there.
/// </summary>
public sealed class PipelineOwnershipTests
{
    private static readonly Guid Channel = Guid.Parse("019f7000-0000-7000-8000-000000000001");
    private static readonly Guid OtherChannel = Guid.Parse("019f7000-0000-7000-8000-000000000002");

    [Fact]
    public async Task Null_pipeline_id_succeeds()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();

        Result result = await db.EnsurePipelineInChannelAsync(Channel, null);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Empty_guid_sentinel_succeeds()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();

        Result result = await db.EnsurePipelineInChannelAsync(Channel, Guid.Empty);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task A_live_pipeline_owned_by_the_channel_succeeds()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        Guid pipelineId = Guid.NewGuid();
        db.Pipelines.Add(
            new()
            {
                Id = pipelineId,
                BroadcasterId = Channel,
                Name = "mine",
                TriggerKind = "command",
            }
        );
        await db.SaveChangesAsync();

        Result result = await db.EnsurePipelineInChannelAsync(Channel, pipelineId);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task A_pipeline_owned_by_another_channel_fails_with_the_error_code()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        Guid pipelineId = Guid.NewGuid();
        db.Pipelines.Add(
            new()
            {
                Id = pipelineId,
                BroadcasterId = OtherChannel,
                Name = "not mine",
                TriggerKind = "command",
            }
        );
        await db.SaveChangesAsync();

        Result result = await db.EnsurePipelineInChannelAsync(Channel, pipelineId);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(PipelineOwnership.ErrorCode);
    }

    [Fact]
    public async Task A_soft_deleted_pipeline_in_the_same_channel_fails_with_the_error_code()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        Guid pipelineId = Guid.NewGuid();
        db.Pipelines.Add(
            new()
            {
                Id = pipelineId,
                BroadcasterId = Channel,
                Name = "deleted",
                TriggerKind = "command",
                DeletedAt = DateTime.UtcNow,
            }
        );
        await db.SaveChangesAsync();

        Result result = await db.EnsurePipelineInChannelAsync(Channel, pipelineId);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(PipelineOwnership.ErrorCode);
    }
}
