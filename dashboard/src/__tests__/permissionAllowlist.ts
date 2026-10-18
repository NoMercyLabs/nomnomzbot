// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// The action controls (Button, Switch, Checkbox and the like) under views/dashboard that sit under no
// can(...) check in their own template or in every parent that mounts them, and the reason each is fine.
// A file lists how many such controls it may hold. A new control in a listed file raises the count and
// fails the test, so it must be gated or the entry must be changed on purpose. A stale count fails too.
// Every group is a real exception. A write control never belongs here: gate it with its own write key.

export interface AllowlistGroup {
  reason: string;
  // Path under views/dashboard, then how many ungated controls the file may hold.
  files: Readonly<Record<string, number>>;
}

export const permissionAllowlist = [
  {
    reason:
      'Local-only UI that changes no stored data: pager, back, close, retry, copy link, panel toggle, display preference, row pick, lookup, dev-only catalog page.',
    files: {
      'admin/AdminChannelsView.vue': 2,
      'admin/AdminContentView.vue': 1,
      'admin/AdminSupportView.vue': 1,
      'admin/AdminUsersView.vue': 2,
      'admin/components/AdminPager.vue': 2,
      'admin/components/ChannelsTable.vue': 1,
      'admin/components/PlatformDefaultPreview.vue': 2,
      'catalog/CatalogView.vue': 11,
      'chat/components/ChatDisplayPanel.vue': 2,
      'chat/components/ChatDockHeader.vue': 6,
      'chat/ChatView.vue': 7,
      'chat/components/ChatLine.vue': 1,
      'home/components/PollDialog.vue': 1,
      'home/components/PredictionDialog.vue': 1,
      'scripts/ScriptsView.vue': 1,
      'channel-points/components/RedemptionsPanel.vue': 1,
      'home/HomeView.vue': 1,
      'people/PeopleView.vue': 1,
      'pipelines/components/StepPicker.vue': 1,
      'scripts/components/FailedRunDialog.vue': 1,
      'settings/ReadabilityView.vue': 1,
      'quotes/QuotesView.vue': 1,
      'settings/event-journal/components/JournalListPanel.vue': 3,
      'settings/api-devices/components/SecretDialog.vue': 2,
      'settings/custom-event-feeds/components/EventFeedRow.vue': 1,
      'settings/webhooks/components/InboundWebhookRow.vue': 1,
      'settings/billing/components/InvoicesPanel.vue': 3,
      'voice-triggers/VoiceTriggersView.vue': 1,
    },
  },
  {
    reason:
      'Edits an in-memory draft or list inside a form; the save control of that form is the write, and it sits in the parent editor.',
    files: {
      'commands/components/RandomReplyList.vue': 2,
      'overlays/components/SettingGroup.vue': 1,
      'overlays/components/StringListEditor.vue': 4,
      'pipelines/components/ListField.vue': 2,
      'pipelines/components/MapField.vue': 2,
      'pipelines/components/StepField.vue': 1,
      'pipelines/components/StepInspector.vue': 2,
      'settings/custom-event-feeds/components/FieldMapEditor.vue': 2,
    },
  },
  {
    reason:
      'Own-data action: the page is about the signed-in person (open: true on its route), not a channel, so no channel key applies. The external chat sends as the person on a Twitch channel the bot does not serve.',
    files: {
      'chat/ExternalChatView.vue': 2,
      'settings/your-data/components/ErasePanel.vue': 2,
      'settings/your-data/components/ExportPanel.vue': 1,
    },
  },
] as const satisfies readonly AllowlistGroup[];
