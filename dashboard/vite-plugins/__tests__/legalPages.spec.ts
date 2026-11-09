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

import { describe, expect, it } from 'vitest';

import { legalPages } from '../legalPages';

type EmittedFile = { fileName: string; source: string };

const shell = '<html><head><title>NomNomzBot</title></head><body><div id="app"></div></body></html>';

function emitted(page: string | null): Map<string, string> {
  const files: EmittedFile[] = [];
  const bundle: Record<string, unknown> = page === null
    ? {}
    : { 'index.html': { type: 'asset', source: page } };
  const hook = legalPages().generateBundle as unknown as {
    handler: (this: unknown, options: unknown, bundle: Record<string, unknown>) => void;
  };

  hook.handler.call({ emitFile: (file: EmittedFile) => files.push(file) }, {}, bundle);

  return new Map(files.map((file) => [file.fileName, file.source]));
}

// Google's check reads the page without running it, so the words must be in the file itself.
describe('the legal pages in the build', () => {
  it('put the whole Privacy Policy in the file, with the facts filled in', () => {
    const page = emitted(shell).get('privacy-policy.html') ?? '';

    expect(page).toContain('<title>Privacy Policy - NomNomzBot</title>');
    expect(page).toContain('<h1>Privacy Policy</h1>');
    expect(page).toContain('Google and YouTube');
    expect(page).toContain('Limited Use');
    expect(page).toContain('https://www.youtube.com/t/terms');
    expect(page).toContain('https://policies.google.com/privacy');
    expect(page).toContain('https://security.google.com/settings/security/permissions');
    expect(page).toContain('legal@nomercy.tv');
    expect(page).toContain('NoMercy Labs');
    expect(page).not.toMatch(/\{\w+\}/);
    expect(page).not.toContain('<div id="app"></div>');
  });

  it('put the Terms of Service in the file, with the law filled in', () => {
    const page = emitted(shell).get('terms-of-service.html') ?? '';

    expect(page).toContain('<h1>Terms of Service</h1>');
    expect(page).toContain('the Netherlands');
    expect(page).not.toMatch(/\{\w+\}/);
  });

  it('keep the app mount point, so the styled page takes over in a browser', () => {
    expect(emitted(shell).get('privacy-policy.html')).toContain('<div id="app"><main>');
  });

  it('escape the words, so a text can never become markup', () => {
    expect(emitted(shell).get('privacy-policy.html')).toContain('(&quot;we&quot;, &quot;us&quot;)');
  });

  it('refuse a build whose page has no mount point, instead of shipping an empty policy', () => {
    expect(() => emitted('<html></html>')).toThrow(/mount point/);
  });

  it('refuse a build without the page', () => {
    expect(() => emitted(null)).toThrow(/index\.html/);
  });
});
