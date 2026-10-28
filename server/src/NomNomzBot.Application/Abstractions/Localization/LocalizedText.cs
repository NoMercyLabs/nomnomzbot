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

namespace NomNomzBot.Application.Abstractions.Localization;

/// <summary>
/// A typed reference to a backend-authored, user-facing translation KEY (S-SCHEMA-I18N-redesign). Backend schema
/// authors (widget settings fields, pipeline action field descriptors) construct one of these per label/help
/// string instead of a bare English literal or string parameter, so a schema-authoring call site cannot
/// accidentally pass raw English text where a translation key belongs. It carries no English or Dutch text —
/// translations live exclusively in the dashboard's existing i18n home
/// (<c>server/i18n/strings/values/strings.xml</c> for <c>en</c>, <c>values-nl/</c> for
/// <c>nl</c>), the single place a translator can find and edit every user-facing string in the product. The
/// dashboard resolves <see cref="Key"/> to display text via <c>core/i18n</c> (dots replaced with underscores to
/// match the Compose Resources string-name convention, e.g. <c>widget.alerts.events.label</c> →
/// <c>widget_alerts_events_label</c>). A committed key manifest (<c>server/i18n/schema-i18n-keys.manifest.json</c>)
/// plus paired backend/frontend guard tests fail the build if a key is authored here without both a <c>en</c> and
/// an <c>nl</c> entry in <c>strings.xml</c> — so a missing translation is caught at test time, never shipped as a
/// silent English fallback or, worse, an empty label. <see cref="Text"/> is user-authored content shown as
/// written, never product text.
/// </summary>
public sealed record LocalizedText(string Key, string? Text = null)
{
    /// <summary>User-authored content shown exactly as written, never looked up and never product text.</summary>
    public static LocalizedText Verbatim(string text) => new(string.Empty, text);
}
