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

namespace NomNomzBot.Application.DevPlatform.Services;

/// <summary>
/// One widget event: the name a widget passes to <c>NomNomz.on(...)</c> and the C# type of the payload the bot
/// sends under that name. A null <paramref name="PayloadType"/> means the frame has no single type (a free-form
/// object), so the generated declarations type it as <c>Record&lt;string, unknown&gt;</c>. A non-null
/// <paramref name="Variants"/> means the name carries several payload shapes told apart by a <c>kind</c> literal, so
/// the declarations type it as the union of them (and <paramref name="PayloadType"/> stays null).
/// </summary>
public sealed record WidgetEventPayloadEntry(
    string Name,
    Type? PayloadType,
    IReadOnlyList<Type>? Variants = null
);

/// <summary>
/// The one truth for "widget event name -> payload type". The widget overlay broadcasters live in the Api project,
/// which Infrastructure cannot reference, so the Api registers this and the SDK type emitter reads it to write the
/// typed <c>NomNomz.on(...)</c> surface a widget author sees in the editor.
/// </summary>
public interface IWidgetEventPayloadRegistry
{
    /// <summary>Every event with a fixed name, in a stable order.</summary>
    IReadOnlyList<WidgetEventPayloadEntry> Events { get; }

    /// <summary>The payload type of every <c>custom.&lt;source&gt;</c> event (a custom data source's own name).</summary>
    Type CustomEventPayloadType { get; }
}
