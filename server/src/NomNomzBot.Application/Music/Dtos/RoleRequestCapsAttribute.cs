// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;

namespace NomNomzBot.Application.Music.Dtos;

/// <summary>
/// Validates the per-role song request caps: only the five rungs song requests know
/// (<see cref="RoleKeys"/>), each cap from 1 to 50 like <c>MaxRequestsPerUser</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class RoleRequestCapsAttribute : ValidationAttribute
{
    public const int MinCap = 1;
    public const int MaxCap = 50;

    /// <summary>The role rungs a cap can be set for, lowest to highest.</summary>
    public static readonly IReadOnlyList<string> RoleKeys =
    [
        "viewer",
        "subscriber",
        "vip",
        "moderator",
        "broadcaster",
    ];

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not IReadOnlyDictionary<string, int> caps)
            return ValidationResult.Success;

        foreach ((string role, int cap) in caps)
        {
            if (!RoleKeys.Contains(role, StringComparer.Ordinal))
                return Fail(
                    $"Unknown role '{role}'. Use one of: {string.Join(", ", RoleKeys)}.",
                    validationContext
                );
            if (cap is < MinCap or > MaxCap)
                return Fail(
                    $"The cap for '{role}' must be between {MinCap} and {MaxCap}.",
                    validationContext
                );
        }
        return ValidationResult.Success;
    }

    private static ValidationResult Fail(string message, ValidationContext context) =>
        new(message, [context.MemberName ?? string.Empty]);
}
