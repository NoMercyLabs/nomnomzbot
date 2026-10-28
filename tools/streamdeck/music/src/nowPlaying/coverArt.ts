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

/**
 * Fetches album art and caches it as a data URI so it can be embedded straight into the Now Playing
 * key's SVG (`<image href="data:...">`) — `setImage` only accepts a single self-contained image, it
 * can't reference an external URL. Keyed by URL: Spotify album art URLs are content-addressed (a
 * given album's art never changes at the same URL), so this never goes stale and never needs
 * invalidation — only a cap on growth for a long-running desktop process.
 */
const cache = new Map<string, string | null>();
const MAX_ENTRIES = 200;

export async function getCoverArtDataUri(url: string | null): Promise<string | null> {
  if (!url) return null;
  const cached = cache.get(url);
  if (cached !== undefined) return cached;

  const dataUri = await fetchAsDataUri(url);
  if (cache.size >= MAX_ENTRIES) cache.delete(cache.keys().next().value as string);
  cache.set(url, dataUri);
  return dataUri;
}

async function fetchAsDataUri(url: string): Promise<string | null> {
  try {
    const res = await fetch(url);
    if (!res.ok) return null;
    const contentType = res.headers.get("content-type") ?? "image/jpeg";
    const bytes = Buffer.from(await res.arrayBuffer());
    return `data:${contentType};base64,${bytes.toString("base64")}`;
  } catch {
    return null;
  }
}
