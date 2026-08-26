// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
}
