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
