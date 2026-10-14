// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import DOMPurify, { type UponSanitizeAttributeHookEvent } from 'dompurify';

import { merge } from '@/lib/array';

// The server sanitizes chat HTML before it sends it (ChatHtmlSanitizer + ChatHtmlSizeLimits). This is the second
// guard in the browser, a port of those rules, so markup that skipped the server still cannot run script on the
// dashboard and renders the same as on the overlay.

const ALLOWED_TAGS = [
  'a', 'abbr', 'b', 'bdi', 'bdo', 'br', 'cite', 'code', 'del', 'dfn', 'em', 'i', 'ins', 'kbd', 'mark', 'q', 's',
  'samp', 'small', 'span', 'strike', 'strong', 'sub', 'sup', 'time', 'u', 'var', 'wbr',
  'blockquote', 'dd', 'div', 'dl', 'dt', 'figcaption', 'figure', 'h1', 'h2', 'h3', 'h4', 'h5', 'h6', 'hr', 'li',
  'ol', 'p', 'pre', 'ul',
  'caption', 'col', 'colgroup', 'table', 'tbody', 'td', 'tfoot', 'th', 'thead', 'tr',
  'img', 'marquee',
  '#text',
];

// No video or audio, and nothing that plays or runs (Stoney 2026-10-14).
const PLAIN_ATTRIBUTES = [
  'alt', 'class', 'colspan', 'dir', 'height', 'lang', 'rowspan', 'style', 'title', 'width',
  'behavior', 'bgcolor', 'direction', 'hspace', 'scrollamount', 'scrolldelay', 'vspace',
];

const URL_ATTRIBUTES = ['href', 'src', 'srcset'];

const ALLOWED_CSS = new Set<string>([
  'width', 'height', 'max-width', 'max-height', 'object-fit', 'color', 'background-color', 'font-size',
  'font-style', 'font-weight', 'letter-spacing', 'line-height', 'text-align', 'text-decoration', 'text-shadow',
]);

const MAX_WIDTH_PX = 500;
const MAX_HEIGHT_PX = 300;

interface LengthCap {
  px: number;
  em: number;
  percent: number;
  unitless: number;
}

// Per property: the largest px, em/rem and % value, and the largest unitless number (0 = only zero).
const LENGTH_CAPS = {
  width: {
    px: MAX_WIDTH_PX,
    em: 30,
    percent: 100,
    unitless: 0,
  },
  'max-width': {
    px: MAX_WIDTH_PX,
    em: 30,
    percent: 100,
    unitless: 0,
  },
  height: {
    px: MAX_HEIGHT_PX,
    em: 18,
    percent: 100,
    unitless: 0,
  },
  'max-height': {
    px: MAX_HEIGHT_PX,
    em: 18,
    percent: 100,
    unitless: 0,
  },
  'font-size': {
    px: 48,
    em: 3,
    percent: 300,
    unitless: 0,
  },
  'line-height': {
    px: 72,
    em: 3,
    percent: 300,
    unitless: 3,
  },
  'letter-spacing': {
    px: 10,
    em: 0.5,
    percent: 0,
    unitless: 0,
  },
} as const satisfies Record<string, LengthCap>;

const LENGTH_KEYWORDS = new Set<string>(['auto', 'normal']);
const CSS_WIDE_KEYWORDS = new Set<string>(['initial', 'inherit', 'unset', 'revert']);
const LENGTH = /^(?<n>-?\d+(?:\.\d+)?)(?<unit>px|em|rem|%)?$/;
const LEADING_NUMBER = /^\s*(?<n>\d+(?:\.\d+)?)(?<pct>%)?/;

const format = (value: number): string =>
  String(Math.round(value * 1000) / 1000);

function isHttps(value: string): boolean {
  return /^https:/i.test(value.trim());
}

function isAllowedUrl(name: string, value: string): boolean {
  if (name !== 'srcset')
    return isHttps(value);

  return value.split(',').every((candidate: string) =>
    isHttps(candidate.trim().split(/\s+/)[0] ?? ''));
}

function isAllowedCssValue(value: string): boolean {
  const lower = value.toLowerCase();

  return !CSS_WIDE_KEYWORDS.has(lower) &&
    !lower.includes('url(') &&
    !lower.includes('expression') &&
    !value.includes('\\') &&
    !value.includes('!');
}

function limitLength(value: string, cap: LengthCap): string | null {
  const lower = value.toLowerCase();

  if (LENGTH_KEYWORDS.has(lower))
    return lower;

  const match: RegExpMatchArray | null = LENGTH.exec(lower);

  if (!match?.groups)
    return null;

  const number = Number(match.groups.n);
  const unit = match.groups.unit ?? '';
  const max = {
    px: cap.px,
    em: cap.em,
    rem: cap.em,
    '%': cap.percent,
  }[unit] ??
  (number === 0
    ? 0
    : cap.unitless);

  if (max === 0 && number !== 0)
    return null;

  const capped = Math.min(Math.abs(number), max);

  return format(Math.sign(number) * capped) + unit;
}

// The inline style with every length capped and every declaration the caps cannot vouch for removed.
function limitStyle(style: string): string | null {
  const kept: string[] = [];

  for (const declaration of style.split(';')) {
    const colon = declaration.indexOf(':');

    if (colon < 0) continue;

    const property: string = declaration.slice(0, colon).trim().toLowerCase();
    const value = declaration.slice(colon + 1).trim();

    if (!ALLOWED_CSS.has(property) || !isAllowedCssValue(value)) continue;

    const cap = (LENGTH_CAPS as Record<string, LengthCap>)[property];
    const limited = cap
      ? limitLength(value, cap)
      : value;

    if (limited !== null) {
      kept.push(`${property}: ${limited}`);
    }
  }

  return kept.length === 0
    ? null
    : kept.join('; ');
}

function limitDimension(name: string, value: string): string | null {
  const match: RegExpMatchArray | null = LEADING_NUMBER.exec(value);

  if (!match?.groups)
    return null;

  const number = Number(match.groups.n);

  if (match.groups.pct)
    return `${format(Math.min(number, 100))}%`;

  const max: number = name === 'height'
    ? MAX_HEIGHT_PX
    : MAX_WIDTH_PX;

  const capped = Math.min(number, max);

  return format(Math.floor(capped));
}

const purifier = DOMPurify();

// An image that lost its url is removed whole, never kept as a broken placeholder.
const lostUrl = new WeakSet<Element>();

function rejectAttribute(element: Element, event: UponSanitizeAttributeHookEvent): void {
  event.keepAttr = false;

  if (event.attrName === 'src' && element.tagName === 'IMG') {
    lostUrl.add(element);
  }
}

function checkAttribute(element: Element, event: UponSanitizeAttributeHookEvent): void {
  const name = event.attrName;

  if (URL_ATTRIBUTES.includes(name)) {
    if (isAllowedUrl(name, event.attrValue)) {
      event.forceKeepAttr = true;
    } else {
      rejectAttribute(element, event);
    }

    return;
  }

  if (name === 'style') {
    const limited = limitStyle(event.attrValue);

    if (limited === null) {
      rejectAttribute(element, event);
    } else {
      event.attrValue = limited;
    }

    return;
  }

  if (name !== 'width' && name !== 'height') return;

  const limited = limitDimension(name, event.attrValue);

  if (limited === null) {
    rejectAttribute(element, event);
  } else {
    event.attrValue = limited;
  }
}

function dropMediaWithoutUrl(node: Node): void {
  if (node instanceof Element && lostUrl.has(node)) {
    node.remove();
  }
}

purifier.addHook('uponSanitizeAttribute', checkAttribute);
purifier.addHook('afterSanitizeAttributes', dropMediaWithoutUrl);

export function purifyChatHtml(html: string): string {
  return purifier.sanitize(html, {
    ALLOWED_TAGS,
    ALLOWED_ATTR: Array.from(merge(PLAIN_ATTRIBUTES, URL_ATTRIBUTES)),
    ADD_URI_SAFE_ATTR: PLAIN_ATTRIBUTES,
    ALLOWED_URI_REGEXP: /^https:/i,
    KEEP_CONTENT: false,
    ALLOW_DATA_ATTR: false,
    ALLOW_ARIA_ATTR: false,
  });
}
