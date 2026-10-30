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
  'dashboard-admin-tenants': 'tenant:read',
  'dashboard-admin-channels-console': 'tenant:read',
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
