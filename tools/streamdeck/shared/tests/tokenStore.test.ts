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

import { describe, it, expect } from "vitest";
import { needsRefresh, isExpired, type PairingState } from "../src/tokenStore.js";

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
