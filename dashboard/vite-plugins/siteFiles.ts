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

import { readFileSync } from 'node:fs';

import type { Plugin } from 'vite';

type HomeWords = { site: { home: { title: string; description: string } } };

const metaMarker: string = '<!-- site:meta -->';

function escapeAttribute(text: string): string {
  return text.replaceAll('&', '&amp;').replaceAll('"', '&quot;').replaceAll('<', '&lt;');
}

// The title, description and Open Graph tags, read from the English home words so the page and its link
// preview say the same thing.
function metaTags(origin: string): string {
  const words = JSON.parse(
    readFileSync('./src/views/site/home/i18n/en.json', 'utf8'),
  ) as HomeWords;
  const title: string = escapeAttribute(words.site.home.title);
  const description: string = escapeAttribute(words.site.home.description);

  return [
    `<title>${title}</title>`,
    `<meta name="description" content="${description}" />`,
    `<meta property="og:type" content="website" />`,
    `<meta property="og:site_name" content="NomNomzBot" />`,
    `<meta property="og:title" content="${title}" />`,
    `<meta property="og:description" content="${description}" />`,
    `<meta property="og:url" content="${origin}/" />`,
    `<meta property="og:image" content="${origin}/pwa-512x512.png" />`,
    `<meta name="twitter:card" content="summary" />`,
  ].join('\n    ');
}

// What static hosting needs next to the page: the page under its two names (index.html for the root,
// 404.html for a deep link, which the host serves for any unknown path), one copy per real page path (the
// host answers a deep link with status 404 unless a file of that name exists, and a real page must say
// 200), the custom domain, no Jekyll and the crawler file.
export function siteFiles(domain: string, pagePaths: readonly string[]): Plugin {
  const origin: string = `https://${domain}`;

  return {
    name: 'nnz:site-files',
    transformIndexHtml: (html: string): string => html.replace(metaMarker, metaTags(origin)),
    // After Vite's own HTML step, which is the one that adds the page to the bundle.
    generateBundle: {
      order: 'post',
      handler(_options, bundle): void {
        const page = bundle['site.html'];

        if (page?.type !== 'asset') throw new Error('site.html is missing from the site bundle');

        delete bundle['site.html'];
        this.emitFile({ type: 'asset', fileName: 'index.html', source: page.source });
        this.emitFile({ type: 'asset', fileName: '404.html', source: page.source });

        for (const path of pagePaths) {
          this.emitFile({ type: 'asset', fileName: `${path}.html`, source: page.source });
        }

        this.emitFile({ type: 'asset', fileName: 'CNAME', source: `${domain}\n` });
        this.emitFile({ type: 'asset', fileName: '.nojekyll', source: '' });
        this.emitFile({
          type: 'asset',
          fileName: 'robots.txt',
          source: `User-agent: *\nAllow: /\n`,
        });
      },
    },
  };
}
