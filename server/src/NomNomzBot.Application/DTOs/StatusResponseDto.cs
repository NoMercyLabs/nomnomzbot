// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace NomNomzBot.Application.DTOs;

/// <summary>
/// Standard response envelope for mutations and single-item responses.
/// Matches the NoMercy media server pattern.
/// </summary>
public sealed class StatusResponseDto<T>
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "ok";

    [JsonPropertyName("data")]
    public T? Data { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>
    /// The failure's machine-readable <c>Result.ErrorCode</c> (e.g. <c>PROVIDER_NOT_CONFIGURED</c>) — absent on
    /// success. Without this the client can only key off the HTTP status, which collapses every 4xx/5xx cause
    /// into one bucket; a caller that needs to branch on WHY (e.g. open a BYOC onboarding dialog instead of a
    /// generic error toast) reads this field.
    /// </summary>
    [JsonPropertyName("code")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Code { get; set; }

    [JsonPropertyName("args")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object[]? Args { get; set; }
}
