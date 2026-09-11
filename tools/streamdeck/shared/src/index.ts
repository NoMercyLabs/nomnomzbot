// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// Barrel for @nomnomzbot/streamdeck-shared — the one copy of the pairing/token/auth layer
// both the music and obs plugins depend on (drift risk otherwise: this is the auth code).
export * from "./tokenStore.js";
export * from "./automationClient.js";
export * from "./deviceFlow.js";
export * from "./deviceFlowState.js";
export * from "./authWindow.js";
export * from "./pairing.js";
