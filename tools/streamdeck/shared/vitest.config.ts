// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import ts from "typescript";
import { defineConfig, type Plugin } from "vitest/config";

const rootDir: string = dirname(fileURLToPath(import.meta.url));

/**
 * Vitest 4 (Vite 8) transforms `.ts` via oxc/rolldown, not esbuild — oxc's parser accepts the
 * TC39 stage-3 `@action(...)` class-decorator syntax `@elgato/streamdeck` actions use, but does not
 * down-level it, and Node has no native runtime support for that syntax at all (confirmed directly:
 * `node` throws `SyntaxError: Invalid or unexpected token` on a bare `@decorator class Foo {}`, even
 * on Node 22). Every action file (`src/actions/*.ts`) is therefore unimportable under vitest's
 * default transform — reproduced on the already-shipped `play.ts`.
 *
 * The production build (`rollup.config.mjs` via `@rollup/plugin-typescript`) doesn't hit this: it
 * runs the real TypeScript compiler, which — per this project's `tsconfig.json` (no
 * `experimentalDecorators`, so TS treats `@action(...)` as a standard/stage-3 decorator) — DOES
 * down-level decorators to `__esDecorate`/`__runInitializers` helper calls for any non-native-decorator
 * target (verified by compiling `play.ts` with `tsc` directly). Only the test toolchain was missing
 * this step. This plugin closes that gap by running the same real compiler, with the same
 * `tsconfig.json`, in place of oxc for every `.ts` file — so tests see the exact JS the production
 * build ships, decorators included.
 *
 * This package (`shared/`) has no `@action`-decorated files of its own, but is kept identical to
 * `music/` and `obs/` for consistency — one vitest transform story across the workspace, and it's
 * harmless here (a plain `tsc` transpile either way).
 */
function decoratorAwareTypescript(): Plugin {
  const configPath: string = join(rootDir, "tsconfig.json");
  const { config, error } = ts.readConfigFile(configPath, ts.sys.readFile);
  if (error) {
    throw new Error(`vitest.config.ts: failed to read ${configPath}: ${ts.flattenDiagnosticMessageText(error.messageText, "\n")}`);
  }
  const { options, errors } = ts.parseJsonConfigFileContent(config, ts.sys, rootDir);
  if (errors.length > 0) {
    throw new Error(
      `vitest.config.ts: failed to parse ${configPath}: ${errors.map((e) => ts.flattenDiagnosticMessageText(e.messageText, "\n")).join("; ")}`,
    );
  }

  return {
    name: "decorator-aware-typescript",
    enforce: "pre",
    transform(code: string, id: string) {
      if (!id.endsWith(".ts")) {
        return null;
      }
      // No source maps: tsc's transpileModule maps back to a single in-memory file with no on-disk
      // sibling, which just produces Vite's "points to missing source files" warning for nothing —
      // debugging a failing test works fine off the emitted JS.
      const result: ts.TranspileOutput = ts.transpileModule(code, {
        compilerOptions: { ...options, sourceMap: false },
        fileName: id,
      });
      return { code: result.outputText };
    },
  };
}

export default defineConfig({
  plugins: [decoratorAwareTypescript()],
  test: {
    environment: "node",
  },
});
