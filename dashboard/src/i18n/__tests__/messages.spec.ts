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

import { baseCompile } from '@intlify/message-compiler';
import { spawnSync } from 'node:child_process';
import { describe, expect, it } from 'vitest';

// Every words file: the shared ones and each page's own.
const files: Record<string, unknown> = import.meta.glob(
  ['/src/i18n/locales/*.json', '/src/**/i18n/*.json'],
  {
    eager: true,
    import: 'default',
  },
);

// The bot's schema labels, generated from the KMP strings by scripts/gen-schema-words.mjs.
const schemaFiles: Record<string, unknown> = import.meta.glob('/src/i18n/schema/*.json', {
  eager: true,
  import: 'default',
});

function messages(node: unknown, path: string, out: [string, string][]): [string, string][] {
  if (typeof node === 'string') {
    out.push([path, node]);
  } else if (node && typeof node === 'object') {
    for (const [key, value] of Object.entries(node)) {
      messages(value, path
        ? `${path}.${key}`
        : key, out);
    }
  }

  return out;
}

// vue-i18n reads { } @ | as syntax, so a text such as "the @mention target" or "(e.g. {args.1})"
// fails to compile and the page shows the key instead. Literal characters need {'@'} and friends.
describe('words files', () => {
  it('finds the shared and the page words files', () => {
    expect(Object.keys(files)).toContain('/src/i18n/locales/en.json');
    expect(Object.keys(files)).toContain('/src/i18n/locales/nl.json');
  });

  it('carries the schema labels as the KMP strings hold them now', () => {
    const check = spawnSync(process.execPath, ['scripts/gen-schema-words.mjs', '--check'], {
      encoding: 'utf8',
    });

    expect(check.stderr).toBe('');
    expect(check.status).toBe(0);
    expect(Object.keys(schemaFiles).sort()).toEqual(['/src/i18n/schema/en.json', '/src/i18n/schema/nl.json']);
  });

  it('holds only messages that vue-i18n can compile', () => {
    const broken: string[] = [];

    for (const [file, words] of Object.entries({ ...files, ...schemaFiles })) {
      for (const [key, text] of messages(words, '', [])) {
        const errors: string[] = [];

        baseCompile(text, { onError: (error: Error) => errors.push(error.message) });

        if (errors.length > 0) {
          broken.push(`${file} ${key}: ${errors[0]}`);
        }
      }
    }

    expect(broken).toEqual([]);
  });

  // A key that only English has shows as a raw key (or English words) to a Dutch reader on the home page.
  it('gives every English key a Dutch text in the file beside it', () => {
    const missing: string[] = [];

    for (const [file, words] of Object.entries(files)) {
      if (!file.endsWith('/en.json')) continue;

      const dutch = files[file.replace(/en\.json$/u, 'nl.json')];
      const have = new Set(messages(dutch ?? {}, '', []).map(([key]) => key));

      for (const [key] of messages(words, '', [])) {
        if (!have.has(key)) {
          missing.push(`${file} ${key}`);
        }
      }
    }

    expect(missing).toEqual([]);
  });

  // Owner rule: a Twitch subscriber and a YouTube subscriber are not the same thing, so a word about
  // subscribing names its platform. A key listed here sits beside a platform logo or names one in context.
  it('names the platform in every English text about subscribing', () => {
    const marked = /Twitch|YouTube|Kick/u;
    const subscribing = /\b(?:re)?sub(?:s|scribe\w*|scription\w*)?\b/iu;
    const besideALogo = new Set(['profile.roles.subscriberBadge']);
    const unmarked: string[] = [];

    for (const [file, words] of Object.entries(files)) {
      if (!file.endsWith('/en.json')) continue;

      for (const [key, text] of messages(words, '', [])) {
        if (subscribing.test(text) && !marked.test(text) && !besideALogo.has(key)) {
          unmarked.push(`${file} ${key}: ${text}`);
        }
      }
    }

    expect(unmarked).toEqual([]);
  });
});
