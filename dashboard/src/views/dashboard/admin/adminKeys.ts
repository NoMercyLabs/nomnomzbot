// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// The platform key each admin page needs to open: the key its main read endpoint checks on the bot
// (Admin*Controller [Authorize(Policy)]). The route guard and the sidebar both read this one table.
export const adminPageKeys = {
  'dashboard-admin-overview': 'platform:analytics:read',
  'dashboard-admin-eventsub-health': 'iam:manage',
  'dashboard-admin-webhook-deliveries': 'iam:manage',
  'dashboard-admin-scheduled-jobs': 'iam:manage',
  'dashboard-admin-tenant-usage': 'iam:manage',
  'dashboard-admin-error-budget': 'iam:manage',
  'dashboard-admin-event-replay': 'iam:manage',
  'dashboard-admin-audit-log': 'audit:read',
  'dashboard-admin-channels': 'tenant:read',
  'dashboard-admin-users': 'iam:manage',
  'dashboard-admin-tenants': 'tenant:read',
  'dashboard-admin-support': 'user:support:view',
  'dashboard-admin-billing': 'billing:read',
  'dashboard-admin-spam-defaults': 'featureflag:write',
  'dashboard-admin-trust-safety': 'trust-safety:review',
  'dashboard-admin-data-requests': 'compliance:erasure',
  'dashboard-admin-system': 'iam:manage',
  'dashboard-admin-feature-flags': 'featureflag:write',
  'dashboard-admin-providers': 'iam:manage',
  'dashboard-admin-content': 'content:read',
  'dashboard-admin-access': 'iam:manage',
  'dashboard-admin-platform-bot': 'platform:bot:manage',
  'dashboard-admin-platform-defaults': 'platform:defaults:manage',
} as const satisfies Record<string, string>;

// The key an admin page needs. A page the table does not name needs no finer key than the admin flag.
export function adminPageKey(routeName: string): string | undefined {
  return (adminPageKeys as Record<string, string>)[routeName];
}
