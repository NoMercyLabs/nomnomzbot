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

import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';

import { guestStarReason } from '@/api/guestStar';

// A refusal of Twitch or of the bot, said in plain words that lead to the fix. A toast tells about a control on
// the page; a dialog shows the same words inside itself, so the person keeps what they typed.
export function useGuestStarFailure() {
  const { t } = useI18n();

  function reasonText(error: Error): string {
    return t(`dashboard.settings.guestStar.errors.${guestStarReason(error)}`);
  }

  function notify(error: Error): void {
    toast.error(reasonText(error));
  }

  return { reasonText, notify } as const satisfies {
    reasonText: (error: Error) => string;
    notify: (error: Error) => void;
  };
}
