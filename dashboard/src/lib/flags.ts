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

// Build-time switches, read from the VITE_ environment. One function per switch, each off by default.

// Sign-in is closed ("Coming soon") on the public pages unless a build sets VITE_SIGN_IN_OPEN=true.
// A self-hosted build and a development run set it, so the sign-in code behind the page keeps working.
export function isSignInOpen(): boolean {
  return import.meta.env.VITE_SIGN_IN_OPEN === 'true';
}
