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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Domain.Identity.Events;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves impersonation start/end reaches the AFFECTED tenant's own dashboard group only — S089d:
/// <c>ImpersonationStartedEvent</c>/<c>ImpersonationEndedEvent</c> had zero consumers, so the tenant owner
/// never learned an operator was acting as one of their users. The tenant + reason are resolved from the
/// backing <c>IamRoleAssignment</c> (<c>ScopeChannelId</c>/<c>Reason</c>) by <c>AccessGrantId</c>, since
/// neither field rides on the event itself. The lookup uses <c>IgnoreQueryFilters()</c> deliberately: ending
/// impersonation is precisely when that grant gets revoked, so the END notification must not silently
/// disappear just because the row is no longer "live" — a dropped notification here would make the audit
/// trail lie about the owner having been told.
/// </summary>
public sealed class ImpersonationBroadcastHandlerTests
{
    private static readonly Guid TargetTenant = Guid.Parse("0192f200-0000-7000-8000-00000000d001");
    private static readonly Guid OtherTenant = Guid.Parse("0192f200-0000-7000-8000-00000000d002");
    private static readonly Guid Operator = Guid.Parse("0192f200-0000-7000-8000-00000000e001");
    private static readonly Guid TargetUser = Guid.Parse("0192f200-0000-7000-8000-00000000c001");
    private static readonly Guid GrantId = Guid.Parse("0192f200-0000-7000-8000-00000000f001");
    private static readonly Guid UnknownGrantId = Guid.Parse(
        "0192f200-0000-7000-8000-00000000f999"
    );

    private static async Task<ImpersonationTestDbContext> SeedGrantAsync(
        Guid? scopeChannelId,
        string? reason,
        DateTime? revokedAt = null
    )
    {
        ImpersonationTestDbContext db = ImpersonationTestDbContext.New();
        db.IamRoleAssignments.Add(
            new()
            {
                Id = GrantId,
                PrincipalId = Operator,
                RoleId = Guid.NewGuid(),
                ScopeChannelId = scopeChannelId,
                Reason = reason,
                RevokedAt = revokedAt,
            }
        );
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task Start_notifies_only_the_target_tenant_with_operator_reason_and_session_id()
    {
        await using ImpersonationTestDbContext db = await SeedGrantAsync(
            TargetTenant,
            "S3 escalation - user locked out"
        );

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
        await new ImpersonationStartedBroadcastHandler(
            notifier,
            db,
            NullLogger<ImpersonationStartedBroadcastHandler>.Instance
        ).HandleAsync(
            new()
            {
                BroadcasterId = Guid.Empty,
                OperatorPrincipalId = Operator,
                TargetUserId = TargetUser,
                AccessGrantId = GrantId,
                ExpiresAt = expiresAt,
            }
        );

        notifiedBroadcaster.Should().Be(TargetTenant.ToString());
        notifiedBroadcaster.Should().NotBe(OtherTenant.ToString());
        sent.Should().NotBeNull();
        sent!.Type.Should().Be("impersonation_started");
        sent.Data!.GetType()
            .GetProperty("OperatorPrincipalId")!
            .GetValue(sent.Data)
            .Should()
            .Be(Operator);
        sent.Data.GetType()
            .GetProperty("TargetUserId")!
            .GetValue(sent.Data)
            .Should()
            .Be(TargetUser);
        sent.Data.GetType().GetProperty("AccessGrantId")!.GetValue(sent.Data).Should().Be(GrantId);
        sent.Data.GetType().GetProperty("ExpiresAt")!.GetValue(sent.Data).Should().Be(expiresAt);
        sent.Data.GetType()
            .GetProperty("Reason")!
            .GetValue(sent.Data)
            .Should()
            .Be("S3 escalation - user locked out");
    }

    [Fact]
    public async Task End_notifies_only_the_target_tenant_with_operator_reason_and_session_id()
    {
        await using ImpersonationTestDbContext db = await SeedGrantAsync(
            TargetTenant,
            "Ticket #4821"
        );

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

        await new ImpersonationEndedBroadcastHandler(
            notifier,
            db,
            NullLogger<ImpersonationEndedBroadcastHandler>.Instance
        ).HandleAsync(
            new()
            {
                BroadcasterId = Guid.Empty,
                OperatorPrincipalId = Operator,
                TargetUserId = TargetUser,
                AccessGrantId = GrantId,
            }
        );

        notifiedBroadcaster.Should().Be(TargetTenant.ToString());
        notifiedBroadcaster.Should().NotBe(OtherTenant.ToString());
        sent.Should().NotBeNull();
        sent!.Type.Should().Be("impersonation_ended");
        sent.Data!.GetType()
            .GetProperty("OperatorPrincipalId")!
            .GetValue(sent.Data)
            .Should()
            .Be(Operator);
        sent.Data.GetType()
            .GetProperty("TargetUserId")!
            .GetValue(sent.Data)
            .Should()
            .Be(TargetUser);
        sent.Data.GetType().GetProperty("AccessGrantId")!.GetValue(sent.Data).Should().Be(GrantId);
        sent.Data.GetType().GetProperty("Reason")!.GetValue(sent.Data).Should().Be("Ticket #4821");
    }

    [Fact]
    public async Task End_still_notifies_the_target_tenant_when_the_grant_is_already_revoked()
    {
        // Ending impersonation is precisely when the grant gets revoked - the exact moment this lookup
        // must NOT come back empty, or the owner never learns the session ended.
        await using ImpersonationTestDbContext db = await SeedGrantAsync(
            TargetTenant,
            "Session complete - no further action",
            revokedAt: new DateTime(2026, 8, 23, 12, 0, 0, DateTimeKind.Utc)
        );

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

        await new ImpersonationEndedBroadcastHandler(
            notifier,
            db,
            NullLogger<ImpersonationEndedBroadcastHandler>.Instance
        ).HandleAsync(
            new()
            {
                BroadcasterId = Guid.Empty,
                OperatorPrincipalId = Operator,
                TargetUserId = TargetUser,
                AccessGrantId = GrantId,
            }
        );

        notifiedBroadcaster.Should().Be(TargetTenant.ToString());
        sent.Should().NotBeNull();
        sent!
            .Data!.GetType()
            .GetProperty("Reason")!
            .GetValue(sent.Data)
            .Should()
            .Be("Session complete - no further action");
    }

    [Fact]
    public async Task Start_sends_nothing_when_the_grant_is_platform_wide_not_tenant_scoped()
    {
        await using ImpersonationTestDbContext db = await SeedGrantAsync(
            scopeChannelId: null,
            reason: null
        );

        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();

        await new ImpersonationStartedBroadcastHandler(
            notifier,
            db,
            NullLogger<ImpersonationStartedBroadcastHandler>.Instance
        ).HandleAsync(
            new()
            {
                BroadcasterId = Guid.Empty,
                OperatorPrincipalId = Operator,
                TargetUserId = TargetUser,
                AccessGrantId = GrantId,
                ExpiresAt = new(2026, 8, 24, 0, 0, 0, DateTimeKind.Utc),
            }
        );

        await notifier
            .DidNotReceive()
            .SendAlertAsync(Arg.Any<string>(), Arg.Any<AlertDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task End_logs_a_warning_instead_of_silently_returning_when_the_grant_id_is_unknown()
    {
        await using ImpersonationTestDbContext db = await SeedGrantAsync(
            TargetTenant,
            "Ticket #4821"
        );

        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        ILogger<ImpersonationEndedBroadcastHandler> logger = Substitute.For<
            ILogger<ImpersonationEndedBroadcastHandler>
        >();

        await new ImpersonationEndedBroadcastHandler(notifier, db, logger).HandleAsync(
            new()
            {
                BroadcasterId = Guid.Empty,
                OperatorPrincipalId = Operator,
                TargetUserId = TargetUser,
                AccessGrantId = UnknownGrantId,
            }
        );

        await notifier
            .DidNotReceive()
            .SendAlertAsync(Arg.Any<string>(), Arg.Any<AlertDto>(), Arg.Any<CancellationToken>());

        logger
            .Received(1)
            .Log(
                LogLevel.Warning,
                Arg.Any<EventId>(),
                Arg.Is<object>(state => state.ToString()!.Contains(UnknownGrantId.ToString())),
                Arg.Any<Exception?>(),
                Arg.Any<Func<object, Exception?, string>>()
            );
    }

    /// <summary>
    /// Regression guard for the assembly scan at <c>Program.cs:194</c>
    /// (<c>AddEventHandlersFromAssembly(typeof(Program).Assembly)</c> → <c>AddOpenGenericHandlers</c>):
    /// both handlers must satisfy the scan's predicate — public, non-abstract, closing
    /// <c>IEventHandler&lt;T&gt;</c> — or they are silently never registered, reproducing the exact S089d gap.
    /// </summary>
    [Theory]
    [InlineData(typeof(ImpersonationStartedBroadcastHandler), typeof(ImpersonationStartedEvent))]
    [InlineData(typeof(ImpersonationEndedBroadcastHandler), typeof(ImpersonationEndedEvent))]
    public void Handler_satisfies_the_assembly_scan_predicate(Type handlerType, Type eventType)
    {
        handlerType.IsPublic.Should().BeTrue();
        handlerType.IsClass.Should().BeTrue();
        handlerType.IsAbstract.Should().BeFalse();
        handlerType.IsSealed.Should().BeTrue();

        Type expectedInterface =
            typeof(NomNomzBot.Domain.Platform.Interfaces.IEventHandler<>).MakeGenericType(
                eventType
            );
        handlerType.GetInterfaces().Should().Contain(expectedInterface);
    }
}
