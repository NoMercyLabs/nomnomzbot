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

import { readdirSync, readFileSync } from 'node:fs';
import { join, relative } from 'node:path';
import { describe, expect, it } from 'vitest';

import { oneOf } from '../lib/array';

// A page or panel error line is one shared <InlineNotice tone="error" compact>, never a hand-written
// <p role="alert" class="... text-destructive">. The scoped folders are swept lane by lane: a later lane adds
// its folder to the list. A field-level or per-row message has no role="alert" and is not matched.
const src = join(__dirname, '..');
const scopedFolders = ['views'] as const satisfies readonly string[];

// A clean file must leave this list.
const allowlist = [
  // BasicsView: stepError is a message under one input (text-xs, next to that field).
  'views/dashboard/settings/BasicsView.vue',
  // LadderPanel: the per-step messages sit under one ladder step.
  'views/dashboard/settings/escalation/components/LadderPanel.vue',
  // PolicyPanel: the default-timeout message sits under that one field.
  'views/dashboard/settings/escalation/components/PolicyPanel.vue',
  // FeaturesView: the rows lane owns it.
  'views/dashboard/settings/features/FeaturesView.vue',
  // AdminFeatureFlagsView: rolloutValid is a message under the one rollout input.
  'views/dashboard/admin/AdminFeatureFlagsView.vue',
  // PlatformDefaultPreview: the failed line shares a flex row with its Retry button.
  'views/dashboard/admin/components/PlatformDefaultPreview.vue',
  // ModToolsPanel: the duration range message sits under one mode input (text-xs).
  'views/dashboard/chat/components/ModToolsPanel.vue',
  // OverlayDetail: the failure and delete-error spans share a button row, where a block would break the row.
  'views/dashboard/overlays/components/OverlayDetail.vue',
  // CatalogCodeEditor: the script error span shares a button row with the editor button.
  'views/dashboard/catalog/components/CatalogCodeEditor.vue',
  // ObsSetupAction: the stranded flag is a span with a status badge that sits beside the connect button.
  'views/dashboard/overlays/components/ObsSetupAction.vue',
] as const satisfies readonly string[];

const openingTag = /<([a-z][\w-]*)\b[^>]*?\brole="alert"[^>]*>/gis;

function vueSources(folder: string): string[] {
  return readdirSync(folder, { withFileTypes: true }).flatMap((entry) => {
    const path = join(folder, entry.name);

    if (entry.isDirectory())
      return entry.name === '__tests__'
        ? []
        : vueSources(path);

    return path.endsWith('.vue')
      ? [relative(src, path).replace(/\\/g, '/')]
      : [];
  });
}

function handWrittenErrorLines(text: string): string[] {
  return Array.from(text.matchAll(openingTag))
    .map((match) => match[0])
    .filter((tag) => tag.includes('text-destructive'));
}

function holdsHandWrittenErrorLine(path: string): boolean {
  const text: string = readFileSync(join(src, path), 'utf8');

  return handWrittenErrorLines(text).length > 0;
}

describe('hand-written destructive lines', () => {
  it('spots a hand-written error line and ignores an InlineNotice', () => {
    const hand = '<p v-if="error" role="alert" class="pt-3 text-sm text-destructive">{{ error }}</p>';
    const shared = '<InlineNotice v-if="error" tone="error" compact class="pt-3">{{ error }}</InlineNotice>';

    expect(handWrittenErrorLines(hand)).toHaveLength(1);
    expect(handWrittenErrorLines(shared)).toEqual([]);
  });

  it('has no hand-written error line in the scoped folders outside the allowlist', () => {
    const offenders = scopedFolders
      .flatMap((folder) => vueSources(join(src, folder)))
      .filter((path) => holdsHandWrittenErrorLine(path))
      .filter((path) => oneOf(allowlist, path) === undefined);

    expect(offenders).toEqual([]);
  });

  it('keeps no clean file on the allowlist', () => {
    const stale = allowlist.filter((path) => !holdsHandWrittenErrorLine(path));

    expect(stale).toEqual([]);
  });
});
