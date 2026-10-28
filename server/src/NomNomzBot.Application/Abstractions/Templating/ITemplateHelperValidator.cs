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

using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Abstractions.Templating;

/// <summary>
/// Save-time guard (S042): rejects a template string that references a helper key which is either
/// unknown to <see cref="TemplateHelperRegistry"/> or not valid for the given
/// <see cref="TemplateHelperContext"/> — e.g. <c>{{args.1}}</c> in an event response, or a typo like
/// <c>{{user.nmae}}</c> anywhere.
/// </summary>
public interface ITemplateHelperValidator
{
    /// <summary>
    /// Validates every <c>{{helper}}</c> placeholder in <paramref name="template"/> against the registry
    /// for <paramref name="context"/>. A null/empty template is always valid (nothing to check).
    /// </summary>
    Result Validate(string? template, TemplateHelperContext context);

    /// <summary>
    /// Same check, additionally accepting the caller's own seeded variables (<paramref name="extraKeys"/>) —
    /// e.g. a built-in reply slot's declared variables such as <c>{track.name}</c>, which are real for that one
    /// reply but are not registry helpers (commands-pipelines.md §11).
    /// </summary>
    Result Validate(
        string? template,
        TemplateHelperContext context,
        IReadOnlyCollection<string> extraKeys
    );
}
