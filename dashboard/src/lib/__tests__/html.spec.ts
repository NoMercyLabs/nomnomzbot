// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { describe, expect, it } from 'vitest';

import { purifyChatHtml } from '@/lib/html';

// The browser guard follows the server's ChatHtmlSanitizer and ChatHtmlSizeLimits rule for rule, so markup that
// skipped the server still comes out the same as the overlay shows it.
const parse = (html: string): HTMLElement => {
  const host: HTMLElement = document.createElement('div');

  host.innerHTML = purifyChatHtml(html);

  return host;
};

describe('purifyChatHtml', () => {
  it('keeps the fun markup the old bot rendered: marquee with its motion, emote images, tables', () => {
    const host = parse(
      '<marquee direction="right" scrollamount="12" bgcolor="#ff0"><img src="https://cdn.test/dino.gif" alt="DinoDance" class="emote"></marquee><table><tr><td colspan="2">hi</td></tr></table>',
    );
    const marquee: Element = host.querySelector('marquee')!;

    expect(marquee.getAttribute('direction')).toBe('right');
    expect(marquee.getAttribute('scrollamount')).toBe('12');
    expect(marquee.getAttribute('bgcolor')).toBe('#ff0');
    expect(marquee.querySelector('img.emote')?.getAttribute('src')).toBe('https://cdn.test/dino.gif');
    expect(host.querySelector('td')?.getAttribute('colspan')).toBe('2');
  });

  it('removes a tag outside the allow-list together with everything inside it', () => {
    const host = parse('<b>ok</b><script>alert(1)</script><iframe src="https://x.test"></iframe><svg onload="alert(2)"><text>hi</text></svg><blink>gone</blink>');

    expect(host.innerHTML).toBe('<b>ok</b>');
  });

  it('strips every handler and id, and keeps only https urls', () => {
    const host = parse(
      '<a href="javascript:alert(1)" id="x" onclick="alert(2)">a</a><a href="https://ok.test">b</a><a href="http://plain.test">c</a>',
    );
    const links: HTMLAnchorElement[] = [...host.querySelectorAll('a')];

    expect(links.map((link: HTMLAnchorElement) => link.getAttribute('href'))).toEqual([null, 'https://ok.test', null]);
    expect(links[0]!.hasAttribute('id')).toBe(false);
    expect(links[0]!.hasAttribute('onclick')).toBe(false);
  });

  it('drops an image whose url was not https, instead of a broken placeholder', () => {
    const host = parse('<img src="http://cdn.test/a.gif" alt="a"><img src="javascript:x" alt="b"><img src="https://cdn.test/c.gif" alt="c">');

    expect([...host.querySelectorAll('img')].map((image: HTMLImageElement) => image.alt)).toEqual(['c']);
  });

  // Stoney 2026-10-14: "NO VIDEO AND AUDIO!!!! NO CODE EXECUTION!!!"
  it('removes video and audio whole, with their sources and tracks', () => {
    const host = parse(
      '<b>ok</b><video src="https://cdn.test/v.mp4" controls><source src="https://cdn.test/v.webm"><track src="https://cdn.test/v.vtt"></video><audio src="https://cdn.test/a.mp3" controls></audio>',
    );

    expect(host.innerHTML).toBe('<b>ok</b>');
    expect(parse('<source src="https://cdn.test/v.webm" type="video/webm">').innerHTML).toBe('');
  });

  it('runs no code: no handler, script url, data url or svg script survives', () => {
    const host = parse(
      '<img src="https://c.test/a.gif" onerror="alert(1)" onload="alert(2)"><a href="vbscript:x">v</a><img src="data:image/png;base64,AAAA" alt="d"><svg><script>alert(3)</script></svg><math><mi xlink:href="javascript:alert(4)">m</mi></math>',
    );

    expect(host.innerHTML).toBe('<img src="https://c.test/a.gif"><a>v</a>');
  });

  it('keeps allowed css, caps its sizes, and drops positioning, urls and unreadable lengths', () => {
    const host = parse(
      '<span style="color: red; position: fixed; font-size: 200px; background-color: url(https://x.test/a.png); width: 90vw; height: 20px">t</span>',
    );

    expect(host.querySelector('span')?.getAttribute('style')).toBe('color: red; font-size: 48px; height: 20px');
  });

  it('caps width and height attributes and drops one it cannot read', () => {
    const images: HTMLImageElement[] = [
      ...parse('<img src="https://c.test/a.gif" width="9000" height="50%"><img src="https://c.test/b.gif" width="wide">').querySelectorAll('img'),
    ];

    expect(images[0]!.getAttribute('width')).toBe('500');
    expect(images[0]!.getAttribute('height')).toBe('50%');
    expect(images[1]!.hasAttribute('width')).toBe(false);
  });
});
