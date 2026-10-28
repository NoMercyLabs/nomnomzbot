// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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
