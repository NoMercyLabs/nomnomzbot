// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Widgets.Entities;

/// <summary>
/// A GLOBAL (non-tenant), soft-deletable catalogue entry a channel can install or clone-to-edit (schema §P.8).
/// Two provenances (<see cref="SourceKind"/>): <c>in_repo</c> — the seeded first-party catalogue, whose source
/// ships in-repo and is copied into <see cref="SourceCode"/> at seed (GitHub columns null); and <c>github</c> —
/// a community submission pinned to <see cref="PinnedCommitSha"/>, reviewed before it earns a verified trust tier.
/// <see cref="TrustTier"/> drives the SaaS render-time CSP tier.
/// </summary>
public class WidgetGalleryItem : SoftDeletableEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Submitter (null for the platform-owned first-party catalogue).</summary>
    public Guid? SubmitterUserId { get; set; }
    public string? SubmitterTwitchUserId { get; set; }
    public string? SubmitterDisplayNameSnapshot { get; set; }

    public string Name { get; set; } = null!;
    public string? Description { get; set; }

    /// <summary><c>vue</c> | <c>react</c> | <c>svelte</c> | <c>vanilla</c>.</summary>
    public string Framework { get; set; } = "vanilla";

    /// <summary><c>first_party</c> | <c>verified_community</c> | <c>unverified</c>.</summary>
    public string TrustTier { get; set; } = "unverified";

    /// <summary><c>in_repo</c> (seeded first-party) | <c>github</c> (community submission).</summary>
    public string SourceKind { get; set; } = "github";

    /// <summary>Stable natural key for idempotent first-party seeding (the widget <c>key</c>, e.g. <c>alerts</c>); null for submissions.</summary>
    public string? NaturalKey { get; set; }

    // GitHub provenance — null for the in-repo first-party catalogue.
    public string? GitHubRepoUrl { get; set; }
    public string? PinnedCommitSha { get; set; }
    public string? PinnedTag { get; set; }

    /// <summary>The curated source install/clone copy from (seeded from the in-repo asset for first-party items).</summary>
    public string? SourceCode { get; set; }

    /// <summary>Default settings applied on install (the item's declared config keys + defaults).</summary>
    public Dictionary<string, object> DefaultSettings { get; set; } = new();

    /// <summary>Events the installed widget subscribes to by default.</summary>
    public List<string> DefaultEventSubscriptions { get; set; } = [];

    /// <summary><c>submitted</c> | <c>in_review</c> | <c>verified</c> | <c>rejected</c>.</summary>
    public string ReviewStatus { get; set; } = "submitted";
    public Guid? ReviewedByUserId { get; set; }
    public string? ReviewNotes { get; set; }
    public DateTime? ReviewedAt { get; set; }

    public bool AvailableInSaaS { get; set; }
    public int InstallCount { get; set; }
}
