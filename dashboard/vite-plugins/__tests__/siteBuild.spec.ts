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

import { describe, expect, it } from 'vitest';
import type { Plugin, UserConfig } from 'vite';

import siteConfig, { siteDomain, sitePages } from '../../vite.site.config';
import { siteFiles } from '../siteFiles';

type EmittedFile = { fileName: string; source: string };

// Runs the plugin's bundle step against a bundle that holds only the built page.
function emittedBy(plugin: Plugin, page: string): { files: EmittedFile[]; bundle: Record<string, unknown> } {
  const files: EmittedFile[] = [];
  const bundle: Record<string, unknown> = { 'site.html': { type: 'asset', source: page } };
  const hook = plugin.generateBundle as unknown as {
    handler: (this: unknown, options: unknown, bundle: Record<string, unknown>) => void;
  };

  hook.handler.call(
    { emitFile: (file: EmittedFile) => files.push(file) },
    {},
    bundle,
  );

  return { files, bundle };
}

describe('the public site build', () => {
  const config: UserConfig = siteConfig;
  const pluginNames: string[] = (config.plugins ?? [])
    .flat()
    .map((plugin) => (plugin && typeof plugin === 'object' && 'name' in plugin ? plugin.name : ''));

  it('writes to dist-site from the site page and carries no service worker or manifest', () => {
    expect(config.build?.outDir).toBe('dist-site');
    expect(String(config.build?.rollupOptions?.input)).toMatch(/site\.html$/);
    expect(pluginNames.some((name) => name.includes('pwa'))).toBe(false);
    expect(pluginNames).toContain('nnz:site-files');
  });

  // The host answers a path with 404 unless a file of that name exists: every real page needs its own file.
  it('has a file for every page the config lists', () => {
    const built = emittedBy(siteFiles(siteDomain, sitePages), 'page');
    const files: string[] = built.files.map((file) => file.fileName);

    expect(sitePages.every((path) => files.includes(`${path}.html`))).toBe(true);
  });

  it('keeps sign-in closed in the site mode', () => {
    const env: string = readFileSync('./.env.site', 'utf8');

    expect(env).toMatch(/^VITE_SIGN_IN_OPEN=false$/m);
  });

  it('serves the page as index.html and 404.html with the domain, the no-Jekyll marker and robots.txt', () => {
    const { files, bundle } = emittedBy(siteFiles('nomnomz.bot', ['login']), '<html>page</html>');
    const byName: Map<string, string> = new Map(files.map((file) => [file.fileName, file.source]));

    expect(bundle['site.html']).toBeUndefined();
    expect(byName.get('index.html')).toBe('<html>page</html>');
    expect(byName.get('404.html')).toBe('<html>page</html>');
    expect(byName.get('login.html')).toBe('<html>page</html>');
    expect(byName.get('CNAME')).toBe('nomnomz.bot\n');
    expect(byName.get('.nojekyll')).toBe('');
    expect(byName.get('robots.txt')).toContain('Allow: /');
  });

  it('fills the title, description and Open Graph tags from the English home words', () => {
    const words = JSON.parse(readFileSync('./src/views/site/home/i18n/en.json', 'utf8')) as {
      site: { home: { title: string; description: string } };
    };
    const transform = siteFiles('nomnomz.bot', []).transformIndexHtml as (html: string) => string;
    const html: string = transform('<head><!-- site:meta --></head>');

    expect(html).toContain(`<title>${words.site.home.title}</title>`);
    expect(html).toContain(`<meta name="description" content="${words.site.home.description}" />`);
    expect(html).toContain(`<meta property="og:title" content="${words.site.home.title}" />`);
    expect(html).toContain('<meta property="og:url" content="https://nomnomz.bot/" />');
    expect(html).not.toContain('site:meta');
  });
});
