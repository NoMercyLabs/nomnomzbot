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

import { describe, it, expect, vi } from "vitest";

// Stoney 2026-11-17, after a GPU crash took down the dev server and with it every Stream Deck key: "we need to
// move the streamdeck plugin url to the production version so it is live without the dev server". A paired
// plugin had no place at all to change its bot address: the pairing page only ran while unpaired.
// The mock factories run at the first (dynamic) import, after these are set, as in authWindow.test.ts.
const openedUrls: string[] = [];
const fakeGlobalSettings: Record<string, unknown> = {
  host: "https://dev.example.test",
  backendUrl: "https://dev.example.test",
  token: "made-up-token",
  tokenExpiresAt: new Date(Date.now() + 3_600_000).toISOString(),
  scopes: [],
};
vi.mock("open", () => ({
  default: vi.fn((url: string) => {
    openedUrls.push(url);
    return Promise.resolve();
  }),
}));
vi.mock("@elgato/streamdeck", () => ({
  default: {
    logger: { info: vi.fn(), warn: vi.fn(), setLevel: vi.fn() },
    settings: {
      getGlobalSettings: vi.fn(() => Promise.resolve({ ...fakeGlobalSettings })),
      setGlobalSettings: vi.fn(() => Promise.resolve()),
    },
  },
}));

describe("a plugin that is already paired", () => {
  it("still serves its pairing page, showing the bot it uses, without opening a browser", async () => {
    const { runDeviceFlowLoop } = await import("../src/pairing.js");
    const { authWindowUrl } = await import("../src/authWindow.js");

    await runDeviceFlowLoop(vi.fn());
    const url = await vi.waitFor(() => {
      const served = authWindowUrl();
      expect(served).not.toBeNull();
      return served as string;
    });
    const page = await fetch(url);

    expect(page.status).toBe(200);
    expect(await page.text()).toContain('value="https://dev.example.test"');
    expect(openedUrls).toEqual([]);
  });
});
