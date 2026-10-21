// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// Build-time switches, read from the VITE_ environment. One function per switch, each off by default.

// Sign-in is closed ("Coming soon") on the public pages unless a build sets VITE_SIGN_IN_OPEN=true.
// A self-hosted build and a development run set it, so the sign-in code behind the page keeps working.
export function isSignInOpen(): boolean {
  return import.meta.env.VITE_SIGN_IN_OPEN === 'true';
}
