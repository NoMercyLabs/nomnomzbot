// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { useI18n } from 'vue-i18n'

// The words for a provider id the server sends (twitch, spotify, edge, ...).
// A provider this client has no name for yet shows its id as the server sent it.
export function useProviderName(): (id: string) => string {
  const { t, te } = useI18n()
  return (id: string): string => (te(`common.provider.${id}`) ? t(`common.provider.${id}`) : id)
}
