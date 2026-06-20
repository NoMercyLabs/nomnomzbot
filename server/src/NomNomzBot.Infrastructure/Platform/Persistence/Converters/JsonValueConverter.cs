// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Newtonsoft.Json;

namespace NomNomzBot.Infrastructure.Platform.Persistence.Converters;

/// <summary>
/// The hand-rolled <c>[VC:JSON]</c> column convention (platform-conventions, twitch-eventsub §1): a typed
/// property is stored as a JSON <c>string</c> column via a Newtonsoft <see cref="ValueConverter{TModel,String}"/>
/// plus a structural <see cref="ValueComparer{T}"/> (so EF change-tracking sees mutations through the string).
/// No <c>jsonb</c>, no <c>HasDefaultValueSql</c> — the converter owns the serialization and the comparer owns
/// snapshot/equality, keeping the mapping provider-agnostic (Postgres + SQLite both store a plain string).
/// </summary>
public static class JsonValueConverter
{
    private static readonly JsonSerializerSettings Settings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
    };

    /// <summary>The converter for <typeparamref name="T"/> ⇄ a JSON <c>string</c> column.</summary>
    public static ValueConverter<T, string> Converter<T>()
        where T : class, new() =>
        new(
            model => JsonConvert.SerializeObject(model, Settings),
            column =>
                string.IsNullOrEmpty(column)
                    ? new T()
                    : JsonConvert.DeserializeObject<T>(column, Settings) ?? new T()
        );

    /// <summary>
    /// A structural comparer for <typeparamref name="T"/> that round-trips through JSON, so EF snapshots and
    /// compares by value (an in-place mutation of a collection/dictionary is detected as a change).
    /// </summary>
    public static ValueComparer<T> Comparer<T>()
        where T : class, new() =>
        new(
            (left, right) =>
                JsonConvert.SerializeObject(left, Settings)
                == JsonConvert.SerializeObject(right, Settings),
            value => JsonConvert.SerializeObject(value, Settings).GetHashCode(),
            value =>
                JsonConvert.DeserializeObject<T>(
                    JsonConvert.SerializeObject(value, Settings),
                    Settings
                ) ?? new T()
        );
}
