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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.Identity.Jobs;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Identity.Jobs;

/// <summary>
/// Proves nothing was expiring platform-support tenant access grants (S086f — <c>BeginTenantAccessAsync</c>
/// created a time-boxed <c>IamRoleAssignment</c> that nothing ever revoked). A single tick must revoke a
/// support grant whose <c>ExpiresAt</c> has passed and leave a still-active grant alone.
/// </summary>
public sealed class TenantAccessGrantExpiryServiceTests
{
    private static readonly Guid Operator = Guid.Parse("0192f000-0000-7000-8000-00000000a001");
    private static readonly Guid Role = Guid.Parse("0192f000-0000-7000-8000-00000000b001");
    private static readonly Guid ExpiredTenant = Guid.Parse("0192f000-0000-7000-8000-00000000c001");
    private static readonly Guid ActiveTenant = Guid.Parse("0192f000-0000-7000-8000-00000000c002");
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_tick_revokes_a_grant_past_its_expiry_and_leaves_an_active_one_alone()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        Guid expiredGrantId = Guid.NewGuid();
        Guid activeGrantId = Guid.NewGuid();
        db.IamRoleAssignments.AddRange(
            new IamRoleAssignment
            {
                Id = expiredGrantId,
                PrincipalId = Operator,
                RoleId = Role,
                ScopeChannelId = ExpiredTenant,
                ExpiresAt = Now.AddMinutes(-1).UtcDateTime,
            },
            new IamRoleAssignment
            {
                Id = activeGrantId,
                PrincipalId = Operator,
                RoleId = Role,
                ScopeChannelId = ActiveTenant,
                ExpiresAt = Now.AddHours(1).UtcDateTime,
            }
        );
        await db.SaveChangesAsync();

        FakeTimeProvider clock = new(Now);
        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(db);
        services.AddSingleton<TimeProvider>(clock);
        ServiceProvider provider = services.BuildServiceProvider();

        TenantAccessGrantExpiryService reaper = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            clock,
            NullLogger<TenantAccessGrantExpiryService>.Instance
        );

        await reaper.TickAsync(CancellationToken.None);

        IamRoleAssignment expired =
            await db.IamRoleAssignments.FindAsync(expiredGrantId)
            ?? throw new InvalidOperationException("expired grant vanished");
        IamRoleAssignment active =
            await db.IamRoleAssignments.FindAsync(activeGrantId)
            ?? throw new InvalidOperationException("active grant vanished");

        expired.RevokedAt.Should().Be(Now.UtcDateTime);
        active.RevokedAt.Should().BeNull();
    }
}
