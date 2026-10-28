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

import { ref, type Ref } from 'vue';

export type HubStatus = 'connected' | 'reconnecting' | 'offline';

export const dashboardHubStatus = ref<HubStatus>('offline');
export const adminHubStatus = ref<HubStatus>('offline');

// A channel the server refused to join (no access, unknown class). Cleared when the channel is joined or left.
export type JoinError = { channelId: string; message: string };

export const dashboardJoinErrors = ref<JoinError[]>([]);

// The state of the dashboard hub, for the frame. The operator hub has its own status for the admin pages.
export function useHubStatus(): Readonly<Ref<HubStatus>> {
  return dashboardHubStatus;
}
