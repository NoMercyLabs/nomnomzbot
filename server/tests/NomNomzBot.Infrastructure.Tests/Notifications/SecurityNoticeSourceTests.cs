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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Notifications.Dtos;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Notifications.Sources;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Notifications;

/// <summary>
/// The owner's read path for security notices: a notice recorded by the impersonation / support-access handlers
/// becomes an inbox item for THAT channel only, naming the operator, the impersonated user and the stated
/// reason, pushes a live inbox refresh, and leaves the inbox once it is acknowledged or dismissed.
/// </summary>
public sealed class SecurityNoticeSourceTests
{
    private static readonly FakeTimeProvider Clock = new(new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero));
    private static readonly Guid Channel = Guid.CreateVersion7();
    private static readonly Guid OtherChannel = Guid.CreateVersion7();

    private static async Task<(
        SecurityNoticeTestDbContext Db,
        Guid NoticeId,
        RecordingChangeNotifier Notifier
    )> RecordImpersonationStartedAsync()
    {
        SecurityNoticeTestDbContext db = SecurityNoticeTestDbContext.New();
        Guid operatorPrincipal = Guid.CreateVersion7();
        Guid target = Guid.CreateVersion7();
        db.IamPrincipals.Add(
            new()
            {
                Id = operatorPrincipal,
                Name = "Support Sam",
                PrincipalType = IamPrincipalType.Employee,
                IsActive = true,
            }
        );
        db.Users.Add(
            new()
            {
                Id = target,
                Username = "mod_mia",
                UsernameNormalized = "mod_mia",
                DisplayName = "Mod Mia",
            }
        );
        await db.SaveChangesAsync();

        RecordingChangeNotifier notifier = new();
        SecurityNoticeService notices = new(db, Clock, notifier);
        Result<SecurityNoticeDto> recorded = await notices.RecordAsync(
            new RecordSecurityNoticeRequest(
                Channel,
                "impersonation_started",
                "A NomNomzBot operator started acting as a user on your channel.",
                operatorPrincipal,
                target,
                Guid.CreateVersion7(),
                "Ticket 4821: commands misfire",
                "channel",
                Clock.GetUtcNow().UtcDateTime.AddHours(1)
            )
        );
        recorded.IsSuccess.Should().BeTrue(recorded.ErrorMessage);
        return (db, recorded.Value.Id, notifier);
    }

    [Fact]
    public async Task A_recorded_impersonation_notice_becomes_a_warning_item_naming_operator_target_and_reason()
    {
        (SecurityNoticeTestDbContext db, Guid noticeId, RecordingChangeNotifier notifier) =
            await RecordImpersonationStartedAsync();

        Result<List<ActionRequiredItemDto>> items = await new SecurityNoticeSource(
            db
        ).GetItemsAsync(Channel, new HashSet<string>());

        notifier
            .Signalled.Should()
            .Equal([Channel], "recording a notice refreshes the channel's inbox live");
        items.IsSuccess.Should().BeTrue(items.ErrorMessage);
        ActionRequiredItemDto item = items.Value.Should().ContainSingle().Subject;
        item.Id.Should().Be($"security-notice:{noticeId}");
        item.Kind.Should().Be("impersonation_started");
        item.Severity.Should().Be("warning");
        item.TitleKey.Should().Be("attention_security_impersonation_started_title");
        item.MessageKey.Should().Be("attention_security_impersonation_started_message");
        item.Parameters.Should()
            .BeEquivalentTo(
                new Dictionary<string, string>
                {
                    ["operatorName"] = "Support Sam",
                    ["targetName"] = "Mod Mia",
                    ["reason"] = "Ticket 4821: commands misfire",
                }
            );
        item.DeepLinkRoute.Should().Be("roles");
    }

    [Fact]
    public async Task Another_channel_never_sees_the_notice()
    {
        (SecurityNoticeTestDbContext db, _, _) = await RecordImpersonationStartedAsync();

        Result<List<ActionRequiredItemDto>> items = await new SecurityNoticeSource(
            db
        ).GetItemsAsync(OtherChannel, new HashSet<string>());

        items.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task An_acknowledged_or_dismissed_notice_leaves_the_inbox()
    {
        (SecurityNoticeTestDbContext db, Guid noticeId, _) =
            await RecordImpersonationStartedAsync();
        SecurityNoticeSource source = new(db);

        (await source.GetItemsAsync(Channel, new HashSet<string> { $"security-notice:{noticeId}" }))
            .Value.Should()
            .BeEmpty("a dismissed notice stays dismissed");

        await new SecurityNoticeService(db, Clock, new RecordingChangeNotifier()).AcknowledgeAsync(
            Channel,
            noticeId,
            Guid.CreateVersion7()
        );
        (await source.GetItemsAsync(Channel, new HashSet<string>()))
            .Value.Should()
            .BeEmpty("an acknowledged notice has been read");
    }
}
