// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Emotes;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.People;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Chat;

/// <summary>What the service-backed providers hand to <see cref="ChatData"/>: the scope factory and the emote seams.</summary>
internal sealed record ChatDataSource(
    IServiceScopeFactory Scopes,
    IViewerDirectory Directory,
    IEmoteChatSource EmoteChat
);
