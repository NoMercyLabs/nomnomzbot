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

import BarChart from '~icons/nnz/bar-chart';
import CalendarTimed from '~icons/nnz/calendar-timed';
import ChatBot from '~icons/nnz/chat-bot';
import ClockBackward from '~icons/nnz/clock-backward';
import CreditCard from '~icons/nnz/credit-card-front';
import FileLines from '~icons/nnz/file-lines';
import Flag from '~icons/nnz/flag';
import Headset from '~icons/nnz/headset';
import KeyModern from '~icons/nnz/key-modern';
import Locked from '~icons/nnz/locked';
import PaperScroll from '~icons/nnz/paper-scroll';
import PaperPlane from '~icons/nnz/paper-plane-send';
import Plugged from '~icons/nnz/plugged';
import Pulse from '~icons/nnz/pulse';
import Server from '~icons/nnz/server';
import SettingsFader from '~icons/nnz/settings-fader';
import Shield from '~icons/nnz/shield';
import ShieldAlert from '~icons/nnz/shield-alert';
import ShieldCheck from '~icons/nnz/shield-check';
import Speedometer from '~icons/nnz/speedometer';
import Tv from '~icons/nnz/tv';
import Users from '~icons/nnz/users';

import { backEntry, navEntry, type NavGroup } from './navigation';

// The platform admin sidebar (mockup base/shell.js NAV.admin). The way back sits first.
export const adminNav: readonly NavGroup[] = [
  {
    key: 'back',
    entries: [backEntry('back.admin')],
  },
  {
    key: 'admin.activity',
    entries: [
      navEntry('dashboard-admin-overview', 'admin.overview', Speedometer),
      navEntry('dashboard-admin-eventsub-health', 'admin.eventsubHealth', Pulse),
      navEntry('dashboard-admin-webhook-deliveries', 'admin.webhookDeliveries', PaperPlane),
      navEntry('dashboard-admin-scheduled-jobs', 'admin.scheduledJobs', CalendarTimed),
      navEntry('dashboard-admin-tenant-usage', 'admin.tenantUsage', BarChart),
      navEntry('dashboard-admin-error-budget', 'admin.errorBudget', ShieldAlert),
      navEntry('dashboard-admin-event-replay', 'admin.eventReplay', ClockBackward),
      navEntry('dashboard-admin-audit-log', 'admin.auditLog', PaperScroll),
    ],
  },
  {
    key: 'admin.people',
    entries: [
      navEntry('dashboard-admin-channels', 'admin.channels', Tv),
      navEntry('dashboard-admin-users', 'admin.users', Users),
      navEntry('dashboard-admin-support', 'admin.support', Headset),
    ],
  },
  {
    key: 'admin.billing',
    entries: [navEntry('dashboard-admin-billing', 'admin.billing', CreditCard)],
  },
  {
    key: 'admin.safety',
    entries: [
      navEntry('dashboard-admin-spam-defaults', 'admin.spamDefaults', Shield),
      navEntry('dashboard-admin-trust-safety', 'admin.trustSafety', ShieldCheck),
      navEntry('dashboard-admin-data-requests', 'admin.dataRequests', Locked),
    ],
  },
  {
    key: 'admin.configuration',
    entries: [
      navEntry('dashboard-admin-system', 'admin.system', Server),
      navEntry('dashboard-admin-feature-flags', 'admin.featureFlags', Flag),
      navEntry('dashboard-admin-providers', 'admin.providers', Plugged),
      navEntry('dashboard-admin-content', 'admin.content', FileLines),
      navEntry('dashboard-admin-access', 'admin.access', KeyModern),
      navEntry('dashboard-admin-platform-bot', 'admin.platformBot', ChatBot),
      navEntry('dashboard-admin-platform-defaults', 'admin.platformDefaults', SettingsFader),
    ],
  },
];
