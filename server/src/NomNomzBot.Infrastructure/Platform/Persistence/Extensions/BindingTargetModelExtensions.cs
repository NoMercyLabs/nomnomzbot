// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Domain.Commands.Enums;
using NomNomzBot.Domain.Commands.ValueObjects;

namespace NomNomzBot.Infrastructure.Platform.Persistence.Extensions;

public static class BindingTargetModelExtensions
{
    /// <summary>
    /// Gives every <see cref="IBindingTargetHolder"/> entity the same <c>TargetKind</c> column: stored as an
    /// integer with database default <see cref="BindingTargetKind.Pipeline"/>, so a row written before the
    /// column existed reads as a pipeline target. The sentinel is out of range so EF always writes the value
    /// (the CLR default, <see cref="BindingTargetKind.Command"/>, would otherwise be replaced by the default).
    /// </summary>
    public static void ApplyBindingTargetColumns(this ModelBuilder modelBuilder)
    {
        foreach (
            Type holderType in modelBuilder
                .Model.GetEntityTypes()
                .Select(entityType => entityType.ClrType)
                .Where(clrType => typeof(IBindingTargetHolder).IsAssignableFrom(clrType))
                .ToList()
        )
        {
            modelBuilder
                .Entity(holderType)
                .Property(nameof(IBindingTargetHolder.TargetKind))
                .HasDefaultValue(BindingTargetKind.Pipeline)
                .HasSentinel((BindingTargetKind)(-1));
        }
    }
}
