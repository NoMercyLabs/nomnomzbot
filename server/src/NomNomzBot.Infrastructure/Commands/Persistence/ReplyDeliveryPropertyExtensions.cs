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
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NomNomzBot.Domain.Commands.ValueObjects;

namespace NomNomzBot.Infrastructure.Commands.Persistence;

/// <summary>
/// The one mapping of a reply delivery setting to its column, shared by every surface that sends a reply
/// (commands, event responses, timers, chat triggers) so the four columns cannot drift apart.
/// </summary>
public static class ReplyDeliveryPropertyExtensions
{
    /// <summary>The column every surface stores its <see cref="ReplyDelivery"/> in.</summary>
    public const string ColumnName = "ReplyDeliveryJson";

    /// <summary>
    /// Maps the property to one nullable JSON text column. Null reads as "chat only", so rows and clients from
    /// before the setting existed keep their behavior. The record's value equality is the change comparer.
    /// </summary>
    public static PropertyBuilder<ReplyDelivery?> HasReplyDeliveryColumn(
        this PropertyBuilder<ReplyDelivery?> property
    ) =>
        property
            .HasColumnName(ColumnName)
            .HasConversion(
                v => v == null ? null : ReplyDeliveryJson.Serialize(v),
                v => ReplyDeliveryJson.Deserialize(v)
            );
}
