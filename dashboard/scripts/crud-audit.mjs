// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// Run: node dashboard/scripts/crud-audit.mjs [--gaps] [--json]
// Prints, per entity, which of create / edit / delete the server offers and how many of those
// endpoints the dashboard calls. "ok" = all called, "n/m" = some called, "GAP" = none called,
// "-" = the server does not offer it. --gaps lists only entities with a gap.

import { auditCrud, operations, totals } from './lib/crudAudit.mjs';

const args = new Set(process.argv.slice(2));
const entities = auditCrud();
const shown = args.has('--gaps') ? entities.filter((e) => e.gaps.length > 0) : entities;

if (args.has('--json')) {
  console.log(JSON.stringify({ totals: totals(entities), entities: shown }, null, 2));
  process.exit(0);
}

function cell(entity, operation) {
  const counts = entity.perOperation[operation];

  if (counts === null) return '-';
  if (counts.called === 0) return 'GAP';

  return counts.called === counts.offered ? 'ok' : `${counts.called}/${counts.offered}`;
}

const width = Math.max(...shown.map((e) => e.entity.length), 6);

console.log(`${'entity'.padEnd(width)}  ${operations.map((o) => o.padEnd(6)).join(' ')}  modules`);

for (const entity of shown) {
  const cells = operations.map((operation) => cell(entity, operation).padEnd(6)).join(' ');

  console.log(`${entity.entity.padEnd(width)}  ${cells}  ${entity.modules.join(', ') || '(no dashboard call)'}`);
}

const sum = totals(entities);

console.log('');
console.log(`entities with a create, edit or delete endpoint: ${sum.entities}`);
console.log(`endpoints called by the dashboard: ${sum.endpointsCalled} of ${sum.endpointsOffered}`);
console.log(`entity operations with a dashboard call: ${sum.operationsCalled} of ${sum.operationsOffered}`);
console.log(`entity operations with no dashboard call (gaps): ${sum.operationsOffered - sum.operationsCalled}`);
