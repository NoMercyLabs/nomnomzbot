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
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const rootDir: string = dirname(fileURLToPath(import.meta.url));
const packageDir: string = join(rootDir, "..");

interface ManifestAction {
  UUID: string;
}

interface Manifest {
  Actions: ManifestAction[];
}

// simple-param.html looks up its per-action field list from FIELD_CONFIG, keyed by
// piActionInfo.action — the manifest UUID the Stream Deck app sends at runtime. A
// FIELD_CONFIG key that doesn't byte-match a real manifest UUID silently renders no
// fields at all (found live: every key here was missing the plugin's doubled
// "streamdeck.music.music-..." segment). None of the per-action behavior tests catch
// this because they construct settings objects directly rather than going through the
// property inspector's key lookup.
describe("simple-param.html FIELD_CONFIG keys", () => {
  it("every FIELD_CONFIG key matches a real manifest.json action UUID", () => {
    const manifest: Manifest = JSON.parse(
      readFileSync(join(packageDir, "bot.nomnomzbot.streamdeck.music.sdPlugin/manifest.json"), "utf-8"),
    );
    const manifestUuids: Set<string> = new Set(manifest.Actions.map((a) => a.UUID));

    const html: string = readFileSync(
      join(packageDir, "bot.nomnomzbot.streamdeck.music.sdPlugin/ui/simple-param.html"),
      "utf-8",
    );
    const fieldConfigBlock: RegExpMatchArray | null = html.match(
      /const FIELD_CONFIG = \{([\s\S]*?)\n {6}\};/,
    );
    expect(fieldConfigBlock, "FIELD_CONFIG object not found in simple-param.html").not.toBeNull();

    const keyPattern: RegExp = /"([^"]+)":\s*\[/g;
    const configKeys: string[] = [...fieldConfigBlock![1].matchAll(keyPattern)].map((m) => m[1]);
    expect(configKeys.length).toBeGreaterThan(0);

    const missingFromManifest: string[] = configKeys.filter((k) => !manifestUuids.has(k));
    expect(missingFromManifest, "FIELD_CONFIG key absent from manifest.json").toEqual([]);
  });
});
