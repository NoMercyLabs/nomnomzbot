// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Platform;

/// <summary>
/// S042c seam: marks a persisted entity property that holds user-authored <c>{{helper}}</c> template
/// text — i.e. a value a streamer typed into a form, later resolved by <c>ITemplateResolver</c> at
/// execution time. This is the structural signal <c>TemplatedUserContentSavePathGuardTests</c>
/// (Infrastructure.Tests) reflects over to enumerate every save path that MUST route through
/// <c>ITemplateHelperValidator</c> before persisting, without hand-listing services. A field's absence
/// of this attribute means "not user-authored template text" (e.g. <c>ChatMessage.Message</c> is a
/// logged chat line, not a template) — adding a new templated field to the domain model is what puts
/// it under the guard's coverage; nothing else needs to be told about it by hand.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class TemplatedUserContentAttribute : Attribute;
