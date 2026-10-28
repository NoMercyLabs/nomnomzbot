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
