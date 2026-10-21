// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// A readable name from a key the bot sent without label words: "fontSize" -> "Font size",
// "send_message" -> "Send message".
export function humanize(key: string): string {
  const words = key
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/[_.-]+/g, ' ')
    .trim()
    .toLowerCase();

  return words.charAt(0).toUpperCase() + words.slice(1);
}

// "Name: value" lines to a name/value map. A blank line is skipped; a line with no colon or no name is
// returned in `invalid` so the caller can say so instead of dropping it.
export function parseHeaderLines(text: string) {
  const headers: Record<string, string> = {};
  const invalid: string[] = [];

  for (const raw of text.split('\n')) {
    const line = raw.trim();
    const colon = line.indexOf(':');

    if (line === '') continue;

    if (colon < 1) {
      invalid.push(line);
      continue;
    }

    headers[line.slice(0, colon).trim()] = line.slice(colon + 1).trim();
  }

  return { headers, invalid } as const satisfies {
    headers: Record<string, string>;
    invalid: string[];
  };
}

// A piece of a line of prose: plain text, or a web address the reader may want to open or copy.
export type LinkPart = {
  kind: 'text' | 'link';
  value: string;
};

const addressPattern = /https?:\/\/[^\s<>"']+/g;
const addressTail = /[.,;:!?)\]]+$/;

// A line of prose to its text and web address parts, in order. The punctuation that ends a sentence
// ("...dashboard.") belongs to the text after the address, not to the address.
export function splitLinks(line: string): LinkPart[] {
  const parts: LinkPart[] = [];
  let from = 0;

  for (const match of line.matchAll(addressPattern)) {
    const link = match[0].replace(addressTail, '');

    if (match.index > from) {
      parts.push({
        kind: 'text',
        value: line.slice(from, match.index),
      });
    }

    parts.push({
      kind: 'link',
      value: link,
    });

    from = match.index + link.length;
  }

  if (from < line.length) {
    parts.push({
      kind: 'text',
      value: line.slice(from),
    });
  }

  return parts;
}

// Two letters for an avatar without a picture: "Stoney_Eagle" -> "ST".
export function initials(name: string): string {
  return name
    .replace(/[^A-Za-z0-9]/g, '')
    .slice(0, 2)
    .toUpperCase();
}
