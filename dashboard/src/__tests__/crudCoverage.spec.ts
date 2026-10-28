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

import { auditCrud, type CrudEntity } from '../../scripts/lib/crudAudit.mjs';
import { crudBaseline } from './crudBaseline';
import { crudExceptions } from './crudExceptions';

// S-CRUD-COMPLETE guard: every create, edit and delete endpoint the server offers is called by
// the dashboard, or is named in crudExceptions.ts (with its reason), or is grandfathered in
// crudBaseline.ts. The baseline only shrinks: a row whose endpoint is now called must be deleted.

type Allowed = Record<string, readonly string[]>;

function uncalled(entities: CrudEntity[]): Map<string, string[]> {
  const map = new Map<string, string[]>();

  for (const entity of entities) {
    const keys = entity.rows.filter((row) => !row.called).map((row) => row.key);

    if (keys.length > 0) {
      map.set(entity.entity, keys);
    }
  }

  return map;
}

function judge(
  entities: CrudEntity[],
  baseline: Allowed,
  exceptions: Record<string, Record<string, string>>,
): string[] {
  const problems: string[] = [];
  const open = uncalled(entities);
  const known = new Map(entities.map((entity) => [entity.entity, entity.rows.map((r) => r.key)]));

  for (const [entity, keys] of open) {
    for (const key of keys) {
      const excepted = exceptions[entity]?.[key] !== undefined;
      const grandfathered = baseline[entity]?.includes(key) === true;

      if (!excepted && !grandfathered) {
        problems.push(`${entity}: ${key} has no dashboard call (build it, or add an exception)`);
      }
    }
  }

  for (const [entity, keys] of Object.entries(baseline)) {
    for (const key of keys) {
      if (!known.get(entity)?.includes(key)) {
        problems.push(`${entity}: baseline row "${key}" is not a server endpoint, delete it`);
      } else if (!open.get(entity)?.includes(key)) {
        problems.push(`${entity}: "${key}" is called now, delete its baseline row`);
      }
    }
  }

  for (const [entity, rows] of Object.entries(exceptions)) {
    for (const [key, reason] of Object.entries(rows)) {
      if (reason.trim() === '') {
        problems.push(`${entity}: exception "${key}" has no reason`);
      }

      if (baseline[entity]?.includes(key) === true) {
        problems.push(`${entity}: "${key}" is both an exception and in the baseline`);
      }

      if (!open.get(entity)?.includes(key)) {
        problems.push(`${entity}: exception "${key}" is called or gone, delete it`);
      }
    }
  }

  return problems;
}

function entity(name: string, rows: Array<[string, boolean]>): CrudEntity {
  return {
    entity: name,
    modules: [],
    gaps: [],
    perOperation: {
      create: null,
      edit: null,
      delete: null,
    },
    rows: rows.map(([key, called]) => ({
      key,
      path: key.split(' ')[1] ?? '',
      verb: 'POST',
      operation: 'create',
      called,
      inferred: false,
      files: [],
    })),
  };
}

describe('crud coverage guard', () => {
  it('accepts every uncalled endpoint of the real tree as an exception or a baseline row', () => {
    expect(judge(auditCrud(), crudBaseline, crudExceptions)).toEqual([]);
  });

  it('keeps every exception reason a real sentence', () => {
    const reasons = Object.values(crudExceptions).flatMap((rows) => Object.values(rows));

    expect(reasons.length).toBeGreaterThan(0);
    expect(reasons.every((reason) => reason.trim().length > 20)).toBe(true);
  });

  it('fails for a new uncalled endpoint that is neither excepted nor in the baseline', () => {
    const problems = judge([entity('widgets', [['POST /widgets', false]])], {}, {});

    expect(problems).toEqual([
      'widgets: POST /widgets has no dashboard call (build it, or add an exception)',
    ]);
  });

  it('passes for a grandfathered endpoint and for an excepted one', () => {
    const entities = [
      entity('widgets', [['POST /widgets', false], ['DELETE /widgets/{id}', false]]),
    ];
    const baseline: Allowed = { widgets: ['POST /widgets'] };
    const exceptions = { widgets: { 'DELETE /widgets/{id}': 'The platform owns the widgets.' } };

    expect(judge(entities, baseline, exceptions)).toEqual([]);
  });

  it('fails when a baseline row is called now, so the baseline can only shrink', () => {
    const problems = judge([entity('widgets', [['POST /widgets', true]])], {
      widgets: ['POST /widgets'],
    }, {});

    expect(problems).toEqual(['widgets: "POST /widgets" is called now, delete its baseline row']);
  });

  it('fails for a baseline row that names no server endpoint', () => {
    const problems = judge([], { ghosts: ['POST /ghosts'] }, {});

    expect(problems).toEqual(['ghosts: baseline row "POST /ghosts" is not a server endpoint, delete it']);
  });

  it('fails for an exception without a reason or one that is called now', () => {
    const entities = [entity('widgets', [['POST /widgets', false], ['PUT /widgets/{id}', true]])];
    const exceptions = {
      widgets: { 'POST /widgets': ' ', 'PUT /widgets/{id}': 'The platform owns the widgets.' },
    };

    expect(judge(entities, {}, exceptions)).toEqual([
      'widgets: exception "POST /widgets" has no reason',
      'widgets: exception "PUT /widgets/{id}" is called or gone, delete it',
    ]);
  });
});
