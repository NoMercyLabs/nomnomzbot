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

// P2 "complete the row vocabulary": a row with a title and a control is a SettingRow, SummaryRow or ActionRow
// inside a RowGroup. A view never writes the row class string (min-h-11 items-center, the 8px px-3 py-2 body, or a
// divide-y list) by hand: components/layout owns it. A file below keeps its hand rows for one stated reason. The
// list only shrinks: a clean file must leave it.
const handRow = /min-h-11[^"]*\bitems-center\b|rounded-\[8px\][^"]*\bpx-3 py-2\b|\bdivide-y\b/;

const domainRow = 'a domain row that owns behavior (links, selection, its own actions)';
const formRow = 'a form row with an inline error or a field next to its label';
const helpOnly = 'a help-only row with no title copy';
const laneC = 'admin form or list in the lane C area; it moves when lane C lands';

const allowlist = {
  'views/dashboard/admin/AdminAccessView.vue': laneC,
  'views/dashboard/admin/AdminContentView.vue': laneC,
  'views/dashboard/admin/AdminDataRequestsView.vue': laneC,
  'views/dashboard/admin/AdminEventReplayView.vue': laneC,
  'views/dashboard/admin/AdminFeatureFlagsView.vue': laneC,
  'views/dashboard/admin/AdminProvidersView.vue': laneC,
  'views/dashboard/admin/AdminSupportView.vue': laneC,
  'views/dashboard/admin/AdminSystemView.vue': laneC,
  'views/dashboard/admin/AdminTrustSafetyView.vue': laneC,
  'views/dashboard/admin/components/BillingChannelPanel.vue': laneC,
  'views/dashboard/admin/components/BillingInvitesPanel.vue': laneC,
  'views/dashboard/admin/components/BillingPricedUnitsPanel.vue': laneC,
  'views/dashboard/admin/components/BillingTiersPanel.vue': laneC,
  'views/dashboard/admin/components/EventSubChannelRow.vue': laneC,
  'views/dashboard/admin/components/PlatformActionDefaults.vue': laneC,
  'views/dashboard/admin/components/PlatformBuiltinReplyDefaults.vue': laneC,
  'views/dashboard/admin/components/PlatformEventResponseDefaults.vue': laneC,
  'views/dashboard/admin/components/RunStepTimeline.vue': laneC,
  'views/dashboard/admin/components/SupportActivityPanels.vue': laneC,
  'views/dashboard/admin/components/SupportPersonPanels.vue': laneC,
  'views/dashboard/admin/components/TenantLimitsPanel.vue': laneC,
  'views/dashboard/admin/components/TenantMembersPanel.vue': laneC,
  'views/dashboard/admin/components/TenantsTable.vue': domainRow,
  'views/dashboard/channel-points/components/RedemptionsPanel.vue': domainRow,
  'views/dashboard/event-responses/components/DefaultLineList.vue': domainRow,
  'views/dashboard/event-responses/components/EventResponseTestResult.vue': domainRow,
  'views/dashboard/games/GamesView.vue': domainRow,
  'views/dashboard/giveaways/GiveawaysView.vue': domainRow,
  'views/dashboard/obs/components/OutputsPanel.vue': domainRow,
  'views/dashboard/scripts/components/ScriptRunsPanel.vue': domainRow,
  'views/dashboard/settings/PersonalityView.vue': 'radios that own the selection',
  'views/dashboard/settings/channel-management/ChannelManagementView.vue': helpOnly,
  'views/dashboard/settings/channel-management/components/BotPresenceRow.vue': domainRow,
  'views/dashboard/settings/custom-event-feeds/components/EventFeedRow.vue': domainRow,
  'views/dashboard/settings/escalation/components/LadderPanel.vue': formRow,
  'views/dashboard/settings/escalation/components/PolicyPanel.vue': formRow,
  'views/dashboard/settings/event-journal/components/ReadModelsPanel.vue': helpOnly,
  'views/dashboard/settings/federation/components/FederationChoiceRow.vue': domainRow,
  'views/dashboard/settings/federation/components/FederationPeerRow.vue': domainRow,
  'views/dashboard/settings/webhooks/components/InboundWebhookRow.vue': domainRow,
  'views/dashboard/settings/webhooks/components/OutboundDeliveriesCard.vue': domainRow,
  'views/dashboard/settings/webhooks/components/OutboundWebhookRow.vue': domainRow,
  'views/dashboard/stream/GoLiveChecklist.vue': domainRow,
  'views/dashboard/stream/SchedulePanel.vue': 'lane C schedule editor',
  'views/dashboard/tts/LexiconPanel.vue': formRow,
} as const satisfies Record<string, string>;

const src = join(__dirname, '..');

function vueFiles(folder: string): string[] {
  return readdirSync(folder, { withFileTypes: true }).flatMap((entry) => {
    const path = join(folder, entry.name);

    if (entry.isDirectory() && entry.name !== '__tests__')
      return vueFiles(path);

    return entry.isFile() && entry.name.endsWith('.vue')
      ? [path]
      : [];
  });
}

function viewFiles(): string[] {
  return vueFiles(join(src, 'views')).map((path) =>
    relative(src, path).replaceAll('\\', '/'));
}

function text(path: string): string {
  return readFileSync(join(src, path), 'utf8');
}

const allowed = Object.keys(allowlist);

describe('row vocabulary', () => {
  it('spots a hand-built row, a hand 8px body and a divide-y list', () => {
    expect(handRow.test('<div class="flex min-h-11 flex-wrap items-center gap-3 px-3 py-2">')).toBe(true);
    expect(handRow.test('<div class="rounded-[8px] px-3 py-2">')).toBe(true);
    expect(handRow.test('<ul class="divide-y divide-border">')).toBe(true);
    expect(handRow.test('<ActionRow :title="name"><Button /></ActionRow>')).toBe(false);
  });

  it('writes no row by hand in a view, except the files that state a reason', () => {
    const offenders = viewFiles()
      .filter((path) => handRow.test(text(path)))
      .filter((path) => !allowed.includes(path));

    expect(offenders).toEqual([]);
  });

  it('keeps no clean file on the allowlist', () => {
    const stale = allowed.filter((path) => !handRow.test(text(path)));

    expect(stale).toEqual([]);
  });
});
