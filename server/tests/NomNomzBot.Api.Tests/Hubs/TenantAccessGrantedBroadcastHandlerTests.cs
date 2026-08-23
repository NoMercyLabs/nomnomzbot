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
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Domain.Identity.Events;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves a granted tenant-access support access reaches the AFFECTED tenant's own dashboard group only —
/// S086f: <c>TenantAccessGrantedEvent</c> had zero consumers, so the owner never learned an operator gained
/// access to their channel.
/// </summary>
public sealed class TenantAccessGrantedBroadcastHandlerTests
{
    private static readonly Guid TargetTenant = Guid.Parse("0192f100-0000-7000-8000-00000000d001");
    private static readonly Guid OtherTenant = Guid.Parse("0192f100-0000-7000-8000-00000000d002");
    private static readonly Guid Principal = Guid.Parse("0192f100-0000-7000-8000-00000000e001");
    private static readonly Guid GrantId = Guid.Parse("0192f100-0000-7000-8000-00000000f001");

    [Fact]
    public async Task Grant_notifies_only_the_target_tenant_with_the_grant_details()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        string? notifiedBroadcaster = null;
        AlertDto? sent = null;
        notifier
            .SendAlertAsync(
                Arg.Do<string>(b => notifiedBroadcaster = b),
                Arg.Do<AlertDto>(d => sent = d),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.CompletedTask);

        DateTime expiresAt = new(2026, 8, 24, 0, 0, 0, DateTimeKind.Utc);
        await new TenantAccessGrantedBroadcastHandler(notifier).HandleAsync(
            new()
            {
                BroadcasterId = Guid.Empty,
                PrincipalId = Principal,
                TargetBroadcasterId = TargetTenant,
                AccessGrantId = GrantId,
                BreakGlass = false,
                ExpiresAt = expiresAt,
            }
        );

        notifiedBroadcaster.Should().Be(TargetTenant.ToString());
        notifiedBroadcaster.Should().NotBe(OtherTenant.ToString());
        sent.Should().NotBeNull();
        sent!.Type.Should().Be("tenant_access_granted");
        sent.Data!.GetType().GetProperty("PrincipalId")!.GetValue(sent.Data).Should().Be(Principal);
        sent.Data.GetType().GetProperty("AccessGrantId")!.GetValue(sent.Data).Should().Be(GrantId);
        sent.Data.GetType().GetProperty("ExpiresAt")!.GetValue(sent.Data).Should().Be(expiresAt);
    }

    [Fact]
    public async Task Break_glass_grant_calls_it_out_in_the_message()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        AlertDto? sent = null;
        notifier
            .SendAlertAsync(
                Arg.Any<string>(),
                Arg.Do<AlertDto>(d => sent = d),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.CompletedTask);

        await new TenantAccessGrantedBroadcastHandler(notifier).HandleAsync(
            new()
            {
                BroadcasterId = Guid.Empty,
                PrincipalId = Principal,
                TargetBroadcasterId = TargetTenant,
                AccessGrantId = GrantId,
                BreakGlass = true,
                ExpiresAt = null,
            }
        );

        sent!.Message.Should().Contain("break-glass");
    }
}
