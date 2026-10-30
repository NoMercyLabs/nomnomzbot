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
      'admin/AdminAccessView.vue': 1,
      'admin/AdminContentView.vue': 1,
      'admin/AdminPlatformDefaultsView.vue': 1,
      'admin/AdminSupportView.vue': 1,
      'admin/components/AdminPager.vue': 2,
      'admin/components/AuditFilterPicker.vue': 2,
      'admin/components/AuditFilters.vue': 1,
      'admin/components/BillingInvitesPanel.vue': 1,
      'admin/components/ChannelConsoleHeader.vue': 1,
      'admin/components/PlatformDefaultPreview.vue': 2,
      'admin/components/UsersTable.vue': 1,
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
      'people/PeopleView.vue': 2,
      'pipelines/components/StepPicker.vue': 1,
      'scripts/components/FailedRunDialog.vue': 1,
      'quotes/QuotesView.vue': 1,
      'picklists/PickListsView.vue': 1,
      'sounds/SoundsView.vue': 1,
      'settings/event-journal/components/JournalListPanel.vue': 4,
      'settings/api-devices/components/KeyEventsInfo.vue': 1,
      'settings/api-devices/components/SecretDialog.vue': 2,
      'settings/integrations/IntegrationsView.vue': 1,
      'settings/webhooks/components/SigningSecretDialog.vue': 2,
      'settings/custom-event-feeds/components/EventFeedRow.vue': 1,
      'settings/webhooks/components/InboundWebhookRow.vue': 1,
      'settings/billing/components/InvoicesPanel.vue': 3,
      'voice-triggers/VoiceTriggersView.vue': 1,
      'voice-triggers/components/VoiceListenerHeader.vue': 1,
    },
  },
  {
    reason:
      'Edits an in-memory draft or list inside a form; the save control of that form is the write, and it sits in the parent editor.',
    files: {
      'commands/components/RandomReplyList.vue': 2,
      'overlays/components/SettingGroup.vue': 3,
      'overlays/components/StringListEditor.vue': 4,
      'pipelines/components/ListField.vue': 2,
      'pipelines/components/MapField.vue': 2,
      'pipelines/components/StepField.vue': 3,
      'pipelines/components/StepInspector.vue': 2,
      'settings/custom-event-feeds/components/FieldMapEditor.vue': 2,
    },
  },
  {
    reason:
      'Own-data action: the page is about the signed-in person (open: true on its route), not a channel, so no channel key applies. The external chat sends as the person on a Twitch channel the bot does not serve.',
    files: {
      'chat/ExternalChatView.vue': 3,
      'settings/your-data/components/ErasePanel.vue': 2,
      'settings/your-data/components/ExportPanel.vue': 1,
    },
  },
] as const satisfies readonly AllowlistGroup[];
