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
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.Contracts.PlatformContent;
using NomNomzBot.Application.DTOs.Billing;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.PlatformContent.Entities;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Content.PlatformContent.Templates;
using NomNomzBot.Infrastructure.Platform.Templating;
using NSubstitute;
using DomainTimer = NomNomzBot.Domain.Commands.Entities.Timer;

namespace NomNomzBot.Infrastructure.Tests.Content.PlatformContent;

/// <summary>
/// Plan item A5, the channel side: a copy an update-in-place publish skipped (because the channel edited it) is
/// listed as behind for that channel only, and the channel can take the new version into it by its own choice —
/// through the kind's real save path, gated by the kind's write key, never into another channel's row.
/// Driven with the timer kind; the service is kind-agnostic.
/// </summary>
public sealed class PlatformTemplateUpdateTests : IAsyncDisposable
{
    private const string HydrateV1 =
        """{"name":"Hydrate","messages":["Drink some water, {channel}!"],"intervalMinutes":45,"minChatActivity":5,"fireOnce":false,"isEnabled":true}""";

    private const string HydrateV2 =
        """{"name":"Hydrate","messages":["Water break, {channel}!"],"intervalMinutes":60,"minChatActivity":0,"fireOnce":false,"isEnabled":true}""";

    private readonly PlatformTemplateHarness _h = new();
    private readonly TimerTemplateInstaller _installer;

    public PlatformTemplateUpdateTests()
    {
        IResourceQuotaService quota = Substitute.For<IResourceQuotaService>();
        quota
            .GetCurrentCountAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(0L));
        quota
            .CheckAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<long>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
                Result.Success(new QuotaCheckDto(true, call.ArgAt<string>(1), 0, 10, 10))
            );
        TimerManagementService timers = new(
            _h.Db,
            Substitute.For<IEventBus>(),
            quota,
            new TemplateHelperValidator()
        );
        _installer = new(_h.Db, timers, new TemplateHelperValidator());
    }

    public ValueTask DisposeAsync() => _h.DisposeAsync();

    /// <summary>
    /// Channel A keeps its copy untouched, channel B edits its copy, then v2 is published update-in-place: A's
    /// copy moves to v2, B's stays on v1 with its edit.
    /// </summary>
    private async Task<Scenario> PublishV2PastAnEditedCopyAsync()
    {
        Channel channelA = await _h.AddChannelAsync("streamer-a");
        Channel channelB = await _h.AddChannelAsync("streamer-b");
        Guid definitionId = await _h.PublishTemplateAsync(_installer, "hydrate", HydrateV1);
        Guid copyOfA = await InstallAsync(channelA, definitionId);
        Guid copyOfB = await InstallAsync(channelB, definitionId);
        DomainTimer edited = await _h.Db.Timers.SingleAsync(t => t.Id == copyOfB);
        edited.IntervalMinutes = 10;
        await _h.Db.SaveChangesAsync();

        Guid v2 = await _h.DraftVersionAsync(_installer, definitionId, HydrateV2);
        Result<PlatformContentPublishJobDto> job = await _h.PublishAfterPreviewAsync(
            _installer,
            definitionId,
            v2,
            PlatformContentPublishModes.UpdateInPlaceWhereUntouched
        );
        job.IsSuccess.Should().BeTrue(job.ErrorMessage);

        return new Scenario(channelA, channelB, definitionId, copyOfA, copyOfB);
    }

    private async Task<Guid> InstallAsync(Channel channel, Guid definitionId) =>
        (
            await _h.Catalog(_installer)
                .InstallAsync(_h.CallerUserId, channel.Id, definitionId, new(null))
        )
            .Value
            .EntityId;

    [Fact]
    public async Task A_copy_the_publish_skipped_is_listed_as_behind_for_its_own_channel_only()
    {
        Scenario s = await PublishV2PastAnEditedCopyAsync();
        _h.Db.Timers.Add(
            new DomainTimer
            {
                BroadcasterId = s.ChannelB.Id,
                Name = "Own timer",
                Messages = ["made by hand"],
            }
        );
        await _h.Db.SaveChangesAsync();
        PlatformTemplateUpdateService updates = _h.Updates(_installer);

        IReadOnlyList<PlatformTemplateUpdateDto> forB = (
            await updates.ListAsync(s.ChannelB.Id, PlatformContentKinds.Timer)
        ).Value;
        IReadOnlyList<PlatformTemplateUpdateDto> forA = (
            await updates.ListAsync(s.ChannelA.Id, PlatformContentKinds.Timer)
        ).Value;

        PlatformTemplateUpdateDto behind = forB.Should().ContainSingle().Which;
        behind.RowId.Should().Be(s.CopyOfB);
        behind.DefinitionId.Should().Be(s.DefinitionId);
        behind.Kind.Should().Be(PlatformContentKinds.Timer);
        behind.DisplayName.Should().Be("hydrate");
        behind.InstalledVersion.Should().Be(1);
        behind.CurrentVersion.Should().Be(2);
        behind.EditedSinceInstall.Should().BeTrue();
        forA.Should().BeEmpty("the publish already moved channel A's untouched copy to v2");
    }

    [Fact]
    public async Task Taking_the_update_rewrites_the_copy_to_the_current_version_and_clears_it_from_the_list()
    {
        Scenario s = await PublishV2PastAnEditedCopyAsync();
        PlatformTemplateUpdateService updates = _h.Updates(_installer);

        Result<PlatformTemplateUpdateDto> applied = await updates.ApplyAsync(
            _h.CallerUserId,
            s.ChannelB.Id,
            s.DefinitionId,
            s.CopyOfB
        );

        applied.IsSuccess.Should().BeTrue(applied.ErrorMessage);
        applied.Value.InstalledVersion.Should().Be(2);
        applied.Value.EditedSinceInstall.Should().BeFalse();
        DomainTimer row = await _h.Db.Timers.AsNoTracking().SingleAsync(t => t.Id == s.CopyOfB);
        row.Messages.Should().Equal("Water break, {channel}!");
        row.IntervalMinutes.Should().Be(60, "the channel chose to replace its edit");
        row.BroadcasterId.Should().Be(s.ChannelB.Id);
        row.PlatformSourceVersion.Should().Be(2);
        row.PlatformSourceHash.Should().Be(TimerTemplatePayload.FromEntity(row).ComputeHash());
        (await updates.ListAsync(s.ChannelB.Id, PlatformContentKinds.Timer))
            .Value.Should()
            .BeEmpty();
    }

    [Fact]
    public async Task An_update_is_refused_for_another_channels_copy_a_current_copy_or_without_the_write_key()
    {
        Scenario s = await PublishV2PastAnEditedCopyAsync();
        PlatformTemplateUpdateService updates = _h.Updates(_installer);

        Result<PlatformTemplateUpdateDto> foreign = await updates.ApplyAsync(
            _h.CallerUserId,
            s.ChannelA.Id,
            s.DefinitionId,
            s.CopyOfB
        );
        Result<PlatformTemplateUpdateDto> current = await updates.ApplyAsync(
            _h.CallerUserId,
            s.ChannelA.Id,
            s.DefinitionId,
            s.CopyOfA
        );
        _h.AllowChannelAction(false);
        Result<PlatformTemplateUpdateDto> denied = await updates.ApplyAsync(
            _h.CallerUserId,
            s.ChannelB.Id,
            s.DefinitionId,
            s.CopyOfB
        );

        // Refused by the service's own provenance-and-channel match, before any kind's save path is reached.
        foreign.ErrorCode.Should().Be("NOT_FOUND");
        foreign.ErrorMessage.Should().Be("This channel has no installed copy of that template.");
        current.ErrorCode.Should().Be("ALREADY_CURRENT");
        denied.ErrorCode.Should().Be("FORBIDDEN");
        DomainTimer untouched = await _h
            .Db.Timers.AsNoTracking()
            .SingleAsync(t => t.Id == s.CopyOfB);
        untouched.IntervalMinutes.Should().Be(10, "no refused update may reach the copy");
        untouched.PlatformSourceVersion.Should().Be(1);
    }

    private sealed record Scenario(
        Channel ChannelA,
        Channel ChannelB,
        Guid DefinitionId,
        Guid CopyOfA,
        Guid CopyOfB
    );
}
