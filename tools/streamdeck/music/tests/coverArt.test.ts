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

import { describe, it, expect, vi, afterEach } from "vitest";
import { getCoverArtDataUri } from "../src/nowPlaying/coverArt.js";

function jsonBytes(contentType: string, bytes: Uint8Array): Response {
  return new Response(bytes, { status: 200, headers: { "Content-Type": contentType } });
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("getCoverArtDataUri", () => {
  it("returns null for no URL", async () => {
    expect(await getCoverArtDataUri(null)).toBeNull();
  });

  it("fetches and base64-encodes the image as a data URI", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonBytes("image/jpeg", new Uint8Array([1, 2, 3])));
    vi.stubGlobal("fetch", fetchMock);

    const uri = await getCoverArtDataUri("https://i.scdn.co/art-unique-1.jpg");

    expect(uri).toBe(`data:image/jpeg;base64,${Buffer.from([1, 2, 3]).toString("base64")}`);
  });

  it("caches by URL so a second call for the same URL doesn't refetch", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonBytes("image/jpeg", new Uint8Array([9])));
    vi.stubGlobal("fetch", fetchMock);

    const url = "https://i.scdn.co/art-unique-2.jpg";
    await getCoverArtDataUri(url);
    await getCoverArtDataUri(url);

    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it("returns null on a fetch failure without throwing", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new Error("network down")));

    const uri = await getCoverArtDataUri("https://i.scdn.co/art-unique-3.jpg");

    expect(uri).toBeNull();
  });
});
