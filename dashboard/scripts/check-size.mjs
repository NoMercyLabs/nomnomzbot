// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
