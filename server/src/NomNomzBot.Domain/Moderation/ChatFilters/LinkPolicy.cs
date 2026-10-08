// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using NomNomzBot.Domain.Moderation.SpamDefense;

namespace NomNomzBot.Domain.Moderation.ChatFilters;

/// <summary>
/// The rules of a <c>ChatFilterType.LinkPolicy</c> filter, stored as JSON in <c>ChatFilter.LinkPolicyJson</c>.
///
/// <para>A link trips the filter unless its host is one of <see cref="AllowedDomains"/> or a sub-domain of one.
/// With no allowed domain every link trips, which is how a filter behaved before it had a policy. Only links
/// written with a scheme (<c>https://x.y</c>) count, unless <see cref="MatchBareDomains"/> is on: then a bare
/// <c>example.com</c> counts too.</para>
/// </summary>
public sealed record LinkPolicy
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Host names whose links are allowed, for example <c>example.com</c>. A sub-domain of an entry is allowed too.</summary>
    public IReadOnlyList<string> AllowedDomains { get; init; } = [];

    /// <summary>Also treat bare domain text with no scheme (<c>example.com/x</c>) as a link.</summary>
    public bool MatchBareDomains { get; init; }

    /// <summary>The policy of a filter that has none: every link trips.</summary>
    public static LinkPolicy Default { get; } = new();

    /// <summary>
    /// Reads a stored policy. Empty, missing or unreadable JSON gives <see cref="Default"/>, so a filter whose
    /// policy cannot be read keeps tripping on every link rather than letting links through.
    /// </summary>
    public static LinkPolicy FromStoredJson(string? json) =>
        TryParse(json, out LinkPolicy policy, out _) ? policy : Default;

    /// <summary>
    /// Parses and validates policy JSON. Empty or blank JSON is the <see cref="Default"/> policy. On failure
    /// <paramref name="error"/> says what is wrong and <paramref name="policy"/> is <see cref="Default"/>.
    /// </summary>
    public static bool TryParse(string? json, out LinkPolicy policy, out string? error)
    {
        policy = Default;
        error = null;
        if (string.IsNullOrWhiteSpace(json))
            return true;

        LinkPolicy? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<LinkPolicy>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            error = $"The link policy is not valid JSON: {ex.Message}";
            return false;
        }

        if (parsed is null)
            return true;

        error = parsed.FindError();
        if (error is not null)
            return false;

        policy = parsed.Normalized();
        return true;
    }

    /// <summary>The first problem with this policy, or null when it is valid.</summary>
    public string? FindError()
    {
        foreach (string? domain in AllowedDomains)
            if (!IsValidHost(domain?.Trim().TrimEnd('.')))
                return $"\"{domain}\" is not a valid domain name. Use a host such as example.com, with no scheme, path or port.";

        return null;
    }

    /// <summary>This policy with every allowed domain trimmed, lower-cased and de-duplicated.</summary>
    public LinkPolicy Normalized() =>
        this with
        {
            AllowedDomains = AllowedDomains
                .Select(d => d.Trim().TrimEnd('.').ToLowerInvariant())
                .Distinct()
                .ToList(),
        };

    /// <summary>The policy as the JSON stored in <c>ChatFilter.LinkPolicyJson</c>.</summary>
    public string ToStoredJson() => JsonSerializer.Serialize(Normalized(), JsonOptions);

    /// <summary>True when <paramref name="message"/> holds at least one link whose host is not allowed.</summary>
    public bool IsTrippedBy(string? message)
    {
        foreach (DetectedLink link in LinkDetector.Find(message))
        {
            if (!MatchBareDomains && !HasScheme(link))
                continue;
            if (!IsAllowed(link.Host))
                return true;
        }

        return false;
    }

    private bool IsAllowed(string host) =>
        AllowedDomains.Any(allowed =>
        {
            string domain = allowed.Trim().TrimEnd('.');
            return host.Equals(domain, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);
        });

    private static bool HasScheme(DetectedLink link) =>
        link.Text.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || link.Text.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    /// <summary>A DNS name with at least two labels. An IP address, a bare TLD, a scheme, a path or a port is not a valid entry.</summary>
    private static bool IsValidHost(string? host) =>
        !string.IsNullOrWhiteSpace(host)
        && host.Contains('.')
        && Uri.CheckHostName(host) == UriHostNameType.Dns;
}
