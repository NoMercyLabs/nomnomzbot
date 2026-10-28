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

// Create / edit / delete coverage: what the server offers (server/openapi/v1.json) against what
// the dashboard calls (the typed client in dashboard/src). Shared by scripts/crud-audit.mjs and
// the guard src/__tests__/crudCoverage.spec.ts. Pure and deterministic: same tree, same result.

import { readdirSync, readFileSync } from 'node:fs';
import { dirname, join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const specPath = join(root, '..', 'server', 'openapi', 'v1.json');
const sourceDir = join(root, 'src');

const apiPrefix = '/api/v{version}/';
const verbs = ['GET', 'POST', 'PUT', 'PATCH', 'DELETE'];
const operationOfVerb = { PUT: 'edit', PATCH: 'edit', DELETE: 'delete' };

export const operations = ['create', 'edit', 'delete'];

const isParam = (segment) => segment.startsWith('{') && segment.endsWith('}');

// Source files that can call the API. Tests and generated declarations never count as a call.
function sourceFiles(dir) {
  const found = [];

  for (const entry of readdirSync(dir, { withFileTypes: true }).sort((a, b) => (a.name < b.name ? -1 : 1))) {
    const full = join(dir, entry.name);

    if (entry.isDirectory()) {
      if (entry.name !== '__tests__') found.push(...sourceFiles(full));
    } else if (/\.(ts|vue)$/.test(entry.name) && !/\.(spec|d)\.ts$/.test(entry.name)) {
      found.push(full);
    }
  }

  return found;
}

// An endpoint's resource: its literal segments, with the channel scope and every {param} dropped.
// /channels/{channelId}/discord/connections/{id}/live-role -> "discord/connections/live-role".
function entityOf(path) {
  const segments = path.slice(apiPrefix.length).split('/');
  const rest = segments[0] === 'channels' && isParam(segments[1] ?? '') ? segments.slice(2) : segments;
  const literal = rest.filter((segment) => !isParam(segment));

  return literal.length === 0 ? 'channels' : literal.join('/');
}

// Reads every {path, verb} the server offers, grouped by entity, and decides which are CRUD.
// POST only counts as "create" on a collection (the entity has an item route or a list route);
// any other POST is an action (skip, revoke, test) and is not a CRUD operation.
function serverEntities() {
  const spec = JSON.parse(readFileSync(specPath, 'utf8'));
  const entities = new Map();

  for (const path of Object.keys(spec.paths).filter((p) => p.startsWith(apiPrefix)).sort()) {
    const entity = entityOf(path);
    const group = entities.get(entity) ?? { name: entity, endpoints: [] };

    for (const verb of verbs.filter((v) => spec.paths[path][v.toLowerCase()] !== undefined)) {
      group.endpoints.push({ path, verb });
    }

    entities.set(entity, group);
  }

  for (const group of entities.values()) {
    const lastIsParam = (path) => isParam(path.split('/').at(-1));
    const hasItemRoute = group.endpoints.some((e) => lastIsParam(e.path));
    const hasList = group.endpoints.some((e) => e.verb === 'GET' && !lastIsParam(e.path));

    group.crud = [];

    for (const { path, verb } of group.endpoints) {
      const operation =
        verb === 'POST' && !lastIsParam(path) && (hasItemRoute || hasList)
          ? 'create'
          : operationOfVerb[verb];

      if (operation !== undefined) group.crud.push({ path, verb, operation });
    }
  }

  return entities;
}

// Template literals with ${} stand for "any one segment": media-share/{id}/${action}.
function literalMatches(literal, path) {
  const want = literal.slice(apiPrefix.length).split('/');
  const have = path.slice(apiPrefix.length).split('/');

  return (
    want.length === have.length &&
    want.every(
      (segment, i) =>
        segment.includes('${') ||
        segment === have[i] ||
        (isParam(segment) && isParam(have[i])),
    )
  );
}

const verbCall = new RegExp(`\\b(${verbs.join('|')})\\(\\s*$`);

// Every dashboard reference to an API path, with the verb it is called with when the call is
// direct: client.POST('/api/...'). A path held in a constant or passed to a helper has no verb
// at the literal; those take the verbs the same file calls, narrowed to what the server offers.
function dashboardReferences() {
  const references = [];
  const literal = /(['"`])(\/api\/v\{version\}\/[^'"`]*)\1/g;

  for (const file of sourceFiles(sourceDir)) {
    const text = readFileSync(file, 'utf8');
    const name = relative(sourceDir, file).split('\\').join('/');
    const fileVerbs = verbs.filter((v) => new RegExp(`\\b${v}\\(`).test(text));

    for (const match of text.matchAll(literal)) {
      const before = text.slice(0, match.index);

      if (/\b(paths|operations)\[\s*$/.test(before)) continue;

      const direct = verbCall.exec(before);

      references.push({
        file: name,
        path: match[2],
        verbs: direct === null ? fileVerbs : [direct[1]],
        inferred: direct === null,
      });
    }
  }

  return references;
}

// The whole picture: per entity, each CRUD endpoint with whether the dashboard calls it.
export function auditCrud() {
  const references = dashboardReferences();
  const entities = [];

  for (const group of serverEntities().values()) {
    if (group.crud.length === 0) continue;

    const rows = group.crud.map(({ path, verb, operation }) => {
      const callers = references.filter(
        (r) => r.verbs.includes(verb) && literalMatches(r.path, path),
      );

      return {
        // "POST /channels/{channelId}/commands": the key the baseline and the exceptions use.
        key: `${verb} ${path.slice(apiPrefix.length - 1)}`,
        path,
        verb,
        operation,
        called: callers.length > 0,
        inferred: callers.length > 0 && callers.every((c) => c.inferred),
        files: [...new Set(callers.map((c) => c.file))].sort(),
      };
    });

    const touching = references
      .filter((r) => group.endpoints.some((e) => literalMatches(r.path, e.path)))
      .map((r) => r.file);

    const perOperation = {};

    for (const operation of operations) {
      const of = rows.filter((row) => row.operation === operation);

      perOperation[operation] =
        of.length === 0 ? null : { offered: of.length, called: of.filter((r) => r.called).length };
    }

    entities.push({
      entity: group.name,
      modules: [...new Set(touching)].sort(),
      rows,
      perOperation,
      // An operation is a gap when the server offers it and no endpoint of it is called.
      gaps: operations.filter(
        (operation) => perOperation[operation] !== null && perOperation[operation].called === 0,
      ),
    });
  }

  return entities.sort((a, b) => (a.entity < b.entity ? -1 : 1));
}

// N of M over endpoints (every offered create/edit/delete) and over entity operations.
export function totals(entities) {
  const rows = entities.flatMap((e) => e.rows);
  const operationsOffered = entities.flatMap((e) =>
    operations.filter((operation) => e.perOperation[operation] !== null),
  );

  return {
    entities: entities.length,
    endpointsOffered: rows.length,
    endpointsCalled: rows.filter((r) => r.called).length,
    operationsOffered: operationsOffered.length,
    operationsCalled: operationsOffered.length - entities.reduce((n, e) => n + e.gaps.length, 0),
  };
}
