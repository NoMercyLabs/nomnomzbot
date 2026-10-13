// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { ref, type Ref } from 'vue'

export type HubStatus = 'connected' | 'reconnecting' | 'offline'

export const dashboardHubStatus: Ref<HubStatus> = ref<HubStatus>('offline')
export const adminHubStatus: Ref<HubStatus> = ref<HubStatus>('offline')

// A channel the server refused to join (no access, unknown class). Cleared when the channel is joined or left.
export type JoinError = { channelId: string; message: string }

export const dashboardJoinErrors: Ref<JoinError[]> = ref<JoinError[]>([])

// The state of the dashboard hub, for the frame. The operator hub has its own status for the admin pages.
export function useHubStatus(): Readonly<Ref<HubStatus>> {
  return dashboardHubStatus
}
