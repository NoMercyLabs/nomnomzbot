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

  // Stoney 2026-11-17: "the account should be listed under accounts in the streamdeck ui too". Stream Deck has
  // no API for its Accounts tab, so the plugin's own page shows the paired channel and can unpair it.
  it("shows the channel it is paired to, and unpairing forgets the pairing", async () => {
    const { authWindowUrl } = await import("../src/authWindow.js");
    const { getPairingState } = await import("../src/tokenStore.js");
    const streamDeck = (await import("@elgato/streamdeck")).default;
    const botCalls: string[] = [];
    const realFetch = globalThis.fetch;
    vi.stubGlobal("fetch", (input: string, init?: RequestInit) => {
      if (!input.startsWith("https://dev.example.test")) return realFetch(input, init);
      botCalls.push(`${init?.method ?? "GET"} ${input} ${new Headers(init?.headers).get("Authorization")}`);
      const data = { channelId: "c1", channelName: "Stoney_Eagle", provider: "twitch", apiVersion: "1" };
      return Promise.resolve(new Response(JSON.stringify({ status: "ok", data })));
    });
    const url = authWindowUrl() as string;

    const page = await (await fetch(url)).text();
    const unpaired = await fetch(`${url}unpair`, { method: "POST" });

    expect(page).toContain("Paired to <b>Stoney_Eagle</b>");
    expect(page).toContain('id="unpair"');
    expect(botCalls).toEqual(["GET https://dev.example.test/automation/v1/info Bearer made-up-token"]);
    expect(unpaired.status).toBe(200);
    expect(await getPairingState()).toBeNull();
    expect(streamDeck.settings.setGlobalSettings).toHaveBeenLastCalledWith({ host: "https://dev.example.test" });
    expect(await (await fetch(url)).text()).not.toContain("Paired to");
    vi.unstubAllGlobals();
  });
});
