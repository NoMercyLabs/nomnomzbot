// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { describe, it, expect } from "vitest";
import { needsRefresh, isExpired, type PairingState } from "../src/connection/tokenStore.js";

function state(tokenExpiresAt: Date): PairingState {
  return {
    backendUrl: "https://bot.example.com",
    token: "nnzb_ak_test",
    tokenExpiresAt: tokenExpiresAt.toISOString(),
    deviceKind: "streamdeck",
  };
}

describe("token lifetime (stream-deck.md D8)", () => {
  const now = new Date("2026-08-11T00:00:00Z");

  it("does not need refresh with 20 days remaining", () => {
    const s = state(new Date(now.getTime() + 20 * 24 * 60 * 60 * 1000));
    expect(needsRefresh(s, now)).toBe(false);
  });

  it("needs refresh under the 7-day threshold", () => {
    const s = state(new Date(now.getTime() + 3 * 24 * 60 * 60 * 1000));
    expect(needsRefresh(s, now)).toBe(true);
  });

  it("is not expired before its ExpiresAt", () => {
    const s = state(new Date(now.getTime() + 1000));
    expect(isExpired(s, now)).toBe(false);
  });

  it("is expired once ExpiresAt has passed", () => {
    const s = state(new Date(now.getTime() - 1000));
    expect(isExpired(s, now)).toBe(true);
  });
});
