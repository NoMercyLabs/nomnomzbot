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

import { readFileSync } from 'node:fs'
import { gzipSync } from 'node:zlib'

const LIMIT_BYTES = 200 * 1024
const html = readFileSync('dist/index.html', 'utf8')
const match = /<script[^>]+src="\/([^"]+\.js)"/.exec(html)
if (!match) {
  console.error('check:size: no entry script found in dist/index.html')
  process.exit(1)
}
const gzipBytes = gzipSync(readFileSync('dist/' + match[1])).length
console.log(`entry chunk ${match[1]}: ${(gzipBytes / 1024).toFixed(1)} KB gzip (limit 200 KB)`)
if (gzipBytes > LIMIT_BYTES) {
  console.error('check:size: FAIL, entry chunk is over 200 KB gzip')
  process.exit(1)
}
