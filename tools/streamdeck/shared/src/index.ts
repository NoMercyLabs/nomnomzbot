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

// Barrel for @nomnomzbot/streamdeck-shared — the one copy of the pairing/token/auth layer
// both the music and obs plugins depend on (drift risk otherwise: this is the auth code).
export * from "./tokenStore.js";
export * from "./automationClient.js";
export * from "./deviceFlow.js";
export * from "./deviceFlowState.js";
export * from "./authWindow.js";
export * from "./pairing.js";
