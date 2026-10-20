// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import AlertTriangle from '~icons/nnz/alert-triangle';
import BookOpen from '~icons/nnz/book-open';
import CellTower from '~icons/nnz/cell-tower';
import ChatBot from '~icons/nnz/chat-bot';
import ChatBubble from '~icons/nnz/chat-bubble';
import ChatLove from '~icons/nnz/chat-love';
import CreditCard from '~icons/nnz/credit-card-front';
import Handshake from '~icons/nnz/handshake';
import KeyOld from '~icons/nnz/key-old';
import Link from '~icons/nnz/link';
import Locked from '~icons/nnz/locked';
import MultiPoint from '~icons/nnz/multi-point-connection';
import PaperScroll from '~icons/nnz/paper-scroll';
import Plugged from '~icons/nnz/plugged';
import SettingsFader from '~icons/nnz/settings-fader';
import ShieldAlert from '~icons/nnz/shield-alert';
import ShieldCheck from '~icons/nnz/shield-check';
import Sparkles from '~icons/nnz/sparkles';
import Scale from '~icons/nnz/scale';
import StairsUp from '~icons/nnz/stairs-up';
import Speedometer from '~icons/nnz/speedometer';
import ToggleOn from '~icons/nnz/toggle-on';

import { backEntry, navEntry, type NavGroup } from './navigation';

// The settings sidebar (mockup 4-settings.html nav rows). Readability is the built settings page.
// The words sit under frame.nav.settingsPages, because frame.nav.settings is the dashboard entry.
export const settingsNav: readonly NavGroup[] = [
  {
    key: 'back',
    entries: [backEntry('back.dashboard')],
  },
  {
    key: 'settings.you',
    entries: [navEntry('dashboard-settings', 'settingsPages.readability', BookOpen, true)],
  },
  {
    key: 'settings.bot',
    entries: [
      navEntry('dashboard-settings-bot-basics', 'settingsPages.botBasics', SettingsFader),
      navEntry('dashboard-settings-bot-personality', 'settingsPages.botPersonality', ChatLove),
      navEntry('dashboard-settings-engagement-triggers', 'settingsPages.engagementTriggers', Sparkles),
      navEntry('dashboard-settings-white-label-bot', 'settingsPages.whiteLabelBot', ChatBot),
      navEntry('dashboard-settings-features', 'settingsPages.features', ToggleOn),
    ],
  },
  {
    key: 'settings.connections',
    entries: [
      navEntry('dashboard-settings-integrations', 'settingsPages.integrations', Plugged),
      navEntry('dashboard-settings-discord', 'settingsPages.discord', ChatBubble),
      navEntry('dashboard-settings-supporter-sources', 'settingsPages.supporterSources', Handshake),
      navEntry('dashboard-settings-federation', 'settingsPages.federation', MultiPoint),
    ],
  },
  {
    key: 'settings.developers',
    entries: [
      navEntry('dashboard-settings-api-devices', 'settingsPages.apiDevices', KeyOld),
      navEntry('dashboard-settings-webhooks', 'settingsPages.webhooks', Link),
      navEntry('dashboard-settings-custom-event-feeds', 'settingsPages.customEventFeeds', CellTower),
    ],
  },
  {
    key: 'settings.access',
    entries: [
      navEntry('dashboard-settings-permissions', 'settingsPages.permissions', ShieldCheck),
      navEntry('dashboard-settings-trust-policy', 'settingsPages.trustPolicy', Scale),
      navEntry('dashboard-settings-escalation', 'settingsPages.escalation', StairsUp),
      navEntry('dashboard-settings-spam-defense', 'settingsPages.spamDefense', ShieldAlert),
      navEntry('dashboard-settings-event-journal', 'settingsPages.eventJournal', PaperScroll),
    ],
  },
  {
    key: 'settings.account',
    entries: [
      navEntry('dashboard-settings-resource-limits', 'settingsPages.resourceLimits', Speedometer),
      navEntry('dashboard-settings-billing', 'settingsPages.billing', CreditCard),
      navEntry('dashboard-settings-your-data', 'settingsPages.yourData', Locked),
      navEntry('dashboard-settings-channel-management', 'settingsPages.channelManagement', AlertTriangle),
    ],
  },
];
