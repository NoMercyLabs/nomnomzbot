// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { readFileSync } from 'node:fs';
import { join } from 'node:path';

import { describe, expect, it } from 'vitest';

import { permissionAllowlist } from './permissionAllowlist';
import { scanControls, type Control } from './permissionScan';

// Stoney 2026-10-18: "every tab and pannel and button and page needs to have proper permission checks before being shown".
// Every action control in a dashboard view sits under a can(...) check, or its file is on the allowlist with a reason.
const allowed = new Map(permissionAllowlist.flatMap((group) => Object.entries(group.files)));

function ungatedPerFile(controls: Control[]): Map<string, Control[]> {
  const perFile = new Map<string, Control[]>();

  for (const control of controls.filter((item) => !item.gated)) {
    perFile.set(control.file, [...(perFile.get(control.file) ?? []), control]);
  }

  return perFile;
}

function describeControl(control: Control): string {
  return `${control.file}:${control.line} <${control.tag}> ${control.signature}`;
}

describe('every action control in a dashboard view carries a permission check', () => {
  const controls = scanControls();
  const ungated = ungatedPerFile(controls);

  it('scans the real views', () => {
    expect(controls.length).toBeGreaterThan(300);
    expect(controls.some((control) => control.gated)).toBe(true);
  });

  it('has no ungated control outside the allowlist', () => {
    const outside = [...ungated]
      .filter(([file]) => !allowed.has(file))
      .flatMap(([, list]) => list.map(describeControl));

    expect(outside).toEqual([]);
  });

  it('has no more ungated controls in a listed file than the allowlist counts', () => {
    const grown = [...ungated]
      .filter(([file, list]) => allowed.has(file) && list.length > (allowed.get(file) ?? 0))
      .flatMap(([, list]) => list.map(describeControl));

    expect(grown).toEqual([]);
  });

  it('keeps no stale allowlist entry once a control is gated or removed', () => {
    const stale = [...allowed]
      .filter(([file, count]) => (ungated.get(file)?.length ?? 0) !== count)
      .map(([file, count]) => `${file}: lists ${count}, has ${ungated.get(file)?.length ?? 0}`);

    expect(stale).toEqual([]);
  });

  // GatedSwitch is not in the scan's control tags on purpose: it carries its own check, so a view that
  // mounts it holds a gated switch without a v-if. If it ever drops the check, this fails.
  it('counts GatedSwitch as gated because it checks the permission itself', () => {
    const source = readFileSync(join(__dirname, '..', 'components', 'layout', 'GatedSwitch.vue'), 'utf8');

    expect(source).toMatch(/can\(action\)/);
    expect(controls.some((control) => control.tag === 'GatedSwitch')).toBe(false);
  });

  it('gives every allowlist group a reason and lists no file twice', () => {
    const files = permissionAllowlist.flatMap((group) => Object.keys(group.files));

    expect(permissionAllowlist.every((group) => group.reason.length > 20)).toBe(true);
    expect(new Set(files).size).toBe(files.length);
  });
});
