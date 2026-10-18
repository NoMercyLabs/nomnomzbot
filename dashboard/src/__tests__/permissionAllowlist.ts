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
// The first two groups are real exceptions. The last two are DEBT: the page is guarded as a whole, and
// the control still needs its own write key. Move a control out of a debt group by gating it.

export interface AllowlistGroup {
  reason: string;
  // Path under views/dashboard, then how many ungated controls the file may hold.
  files: Readonly<Record<string, number>>;
}

export const permissionAllowlist: readonly AllowlistGroup[] = [
  {
    reason: 'Local-only UI that changes no stored data: pager, back, close, retry, copy link, panel toggle, display preference, row pick, lookup, dev-only catalog page.',
    files: {
      'admin/AdminChannelsView.vue': 2,
      'admin/AdminSupportView.vue': 1,
      'admin/AdminUsersView.vue': 2,
      'admin/components/AdminPager.vue': 2,
      'admin/components/ChannelsTable.vue': 1,
      'admin/components/PlatformDefaultPreview.vue': 2,
      'catalog/CatalogView.vue': 11,
      'chat/components/ChatDisplayPanel.vue': 2,
      'chat/components/ChatDockHeader.vue': 6,
      'chat/components/ChatLine.vue': 1,
      'channel-points/components/RedemptionsPanel.vue': 1,
      'home/HomeView.vue': 1,
      'people/PeopleView.vue': 2,
      'pipelines/WorkbenchView.vue': 1,
      'pipelines/components/StepPicker.vue': 1,
      'scripts/components/FailedRunDialog.vue': 1,
      'settings/ReadabilityView.vue': 1,
      'settings/api-devices/components/SecretDialog.vue': 2,
      'settings/billing/components/InvoicesPanel.vue': 3,
      'voice-triggers/VoiceTriggersView.vue': 1,
    },
  },
  {
    reason: 'Edits an in-memory draft or list inside a form; the save control of that form is the write, and it sits in the parent editor.',
    files: {
      'commands/components/RandomReplyDialog.vue': 1,
      'commands/components/RandomReplyList.vue': 2,
      'overlays/components/SettingGroup.vue': 1,
      'overlays/components/StringListEditor.vue': 4,
      'pipelines/components/ListField.vue': 2,
      'pipelines/components/MapField.vue': 2,
      'pipelines/components/StepField.vue': 1,
      'pipelines/components/StepInspector.vue': 2,
      'settings/custom-event-feeds/components/FieldMapEditor.vue': 2,
      'settings/webhooks/components/OutboundWebhookDialog.vue': 1,
    },
  },
  {
    reason: 'DEBT, admin page: the whole page sits behind adminOnly and its platform key; the write control is not yet split out with usePlatformCan.',
    files: {
      'admin/AdminAccessView.vue': 6,
      'admin/AdminContentView.vue': 4,
      'admin/AdminDataRequestsView.vue': 1,
      'admin/AdminEventReplayView.vue': 2,
      'admin/AdminFeatureFlagsView.vue': 9,
      'admin/AdminPlatformBotView.vue': 3,
      'admin/AdminSpamDefaultsView.vue': 2,
      'admin/AdminSystemView.vue': 1,
      'admin/AdminTrustSafetyView.vue': 5,
      'admin/components/PlatformActionDefaults.vue': 1,
      'admin/components/PlatformBuiltinReplyDefaults.vue': 2,
      'admin/components/PlatformEventResponseDefaults.vue': 3,
      'admin/components/PlatformTtsVoiceDefault.vue': 2,
      'admin/components/ProviderCredentialRow.vue': 4,
      'admin/components/TenantActions.vue': 1,
      'admin/components/WebhookDeliveriesTable.vue': 1,
    },
  },
  {
    reason: 'DEBT, channel page: the page is guarded by its route meta.action; the write control (send, save, delete, toggle) is not yet gated with its own write key via useCan.',
    files: {
      'channel-points/components/RewardEditDialog.vue': 3,
      'chat/ChatView.vue': 7,
      'chat/ExternalChatView.vue': 2,
      'chat/components/ViewerPanel.vue': 5,
      'games/components/GameConfigDialog.vue': 2,
      'giveaways/GiveawayDialog.vue': 4,
      'home/components/PollDialog.vue': 7,
      'home/components/PredictionDialog.vue': 7,
      'scripts/ScriptsView.vue': 2,
      'scripts/components/ScriptDetail.vue': 5,
      'scripts/components/ScriptRow.vue': 1,
      'settings/billing/components/InvitePanel.vue': 1,
      'settings/billing/components/PlanPanel.vue': 4,
      'settings/channel-management/ChannelManagementView.vue': 1,
      'settings/channel-management/components/BotPresenceRow.vue': 2,
      'settings/channel-management/components/DeletedChannelNotice.vue': 1,
      'settings/custom-event-feeds/components/EventFeedDialog.vue': 1,
      'settings/custom-event-feeds/components/EventFeedRow.vue': 2,
      'settings/event-journal/components/JournalFilePanel.vue': 2,
      'settings/event-journal/components/LegacyHistoryPanel.vue': 1,
      'settings/event-journal/components/ReadModelsPanel.vue': 2,
      'settings/features/FeaturesView.vue': 1,
      'settings/permissions/components/ActionMatrixPanel.vue': 1,
      'settings/permissions/components/PersonGrantsPanel.vue': 1,
      'settings/permissions/components/TeamRolesPanel.vue': 1,
      'settings/resource-limits/ResourceLimitsView.vue': 1,
      'settings/webhooks/WebhooksView.vue': 1,
      'settings/webhooks/components/InboundWebhookRow.vue': 3,
      'settings/webhooks/components/OutboundWebhookRow.vue': 4,
      'settings/white-label-bot/components/BotLoginPanel.vue': 4,
      'settings/custom-event-feeds/components/TestFeedDialog.vue': 1,
      'stream/RaidRulesPanel.vue': 2,
    },
  },
  {
    reason: 'Own-data action: the page is about the signed-in person (open: true on its route), not a channel, so no channel key applies.',
    files: {
      'settings/your-data/components/ErasePanel.vue': 2,
      'settings/your-data/components/ExportPanel.vue': 1,
    },
  },
];
