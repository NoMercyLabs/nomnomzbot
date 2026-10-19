// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Commands.Services;

/// <summary>
/// Finds the rows on ONE binding surface that run a given command. The command delete and disable paths
/// ask every registered source (through <see cref="ICommandBindingGuard"/>) and refuse when any row is found.
/// <para>
/// Recipe for a surface worker (one class, no other wiring):
/// 1. Add a sealed class in Infrastructure beside the surface's service, for example
///    <c>Infrastructure/Rewards/RewardBindingUsageSource.cs</c>, implementing this interface.
/// 2. Inject <c>IApplicationDbContext</c>. Query the surface's table with <c>AsNoTracking()</c>, filtered by the
///    channel and by the target: kind <c>BindingTargetKind.Command</c> and <c>TargetCommandId.ToString()</c>
///    equal to <c>commandTargetId</c>. A built-in command arrives as its catalogue key (a non-guid text);
///    match it only if the surface can store one.
/// 3. Return one <see cref="BindingUsage"/> per row: a stable lower-case surface key, the row id as text and
///    the name the owner sees. Return an empty list, never null, when nothing binds.
/// 4. Register it in DI next to the surface's other registrations in <c>DependencyInjection.cs</c>:
///    <c>services.AddScoped&lt;IBindingUsageSource, YourSource&gt;();</c>
/// 5. Test it: seed one bound row and one unbound row, assert only the bound row comes back with the right
///    surface, id and name.
/// </para>
/// </summary>
public interface IBindingUsageSource
{
    /// <summary>The rows on this surface in the channel that bind the command.</summary>
    /// <param name="broadcasterId">The channel. Never return rows of another channel.</param>
    /// <param name="commandTargetId">The command id as text, or the built-in catalogue key.</param>
    /// <param name="ct">Cancellation.</param>
    Task<IReadOnlyList<BindingUsage>> FindUsagesAsync(
        Guid broadcasterId,
        string commandTargetId,
        CancellationToken ct = default
    );
}
