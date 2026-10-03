// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.DevPlatform.Services;

/// <summary>
/// One widget event: the name a widget passes to <c>NomNomz.on(...)</c> and the C# type of the payload the bot
/// sends under that name. A null <paramref name="PayloadType"/> means the frame has no single type (a free-form
/// object), so the generated declarations type it as <c>Record&lt;string, unknown&gt;</c>.
/// </summary>
public sealed record WidgetEventPayloadEntry(string Name, Type? PayloadType);

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
