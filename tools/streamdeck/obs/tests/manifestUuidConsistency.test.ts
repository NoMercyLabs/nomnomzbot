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
import { readFileSync, readdirSync } from "node:fs";
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

// The Elgato SDK rejects registerAction() at runtime for any action whose @action({ UUID })
// decorator isn't ALSO present in manifest.json's Actions[] — a silent string-literal drift
// between the two crashes the plugin process on every launch (loops "Process stopped
// (unexpected)" with no signal in the app-level log; the real error only lands in the
// plugin's own per-session log). None of the behavior-level action tests catch this because
// they instantiate action classes directly rather than going through registerAction() — this
// test is a cheap, direct guard against that exact drift.
describe("manifest UUID consistency", () => {
  it("every action UUID declared in src/actions matches one in manifest.json, and vice versa", () => {
    const manifest: Manifest = JSON.parse(
      readFileSync(join(packageDir, "bot.nomnomzbot.streamdeck.obs.sdPlugin/manifest.json"), "utf-8"),
    );
    const manifestUuids: Set<string> = new Set(manifest.Actions.map((a) => a.UUID));

    const actionsDir: string = join(packageDir, "src/actions");
    const sourceUuids: Set<string> = new Set();
    for (const file of readdirSync(actionsDir)) {
      if (!file.endsWith(".ts")) continue;
      const content: string = readFileSync(join(actionsDir, file), "utf-8");
      const match: RegExpMatchArray | null = content.match(/@action\(\{\s*UUID:\s*"([^"]+)"/);
      if (match?.[1] !== undefined) sourceUuids.add(match[1]);
    }

    const missingFromManifest: string[] = [...sourceUuids].filter((u) => !manifestUuids.has(u));
    const missingFromSource: string[] = [...manifestUuids].filter((u) => !sourceUuids.has(u));

    expect(missingFromManifest, "declared in source but absent from manifest.json").toEqual([]);
    expect(missingFromSource, "declared in manifest.json but absent from source").toEqual([]);
  });
});
