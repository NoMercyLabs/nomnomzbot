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

// A streamer who signs in from an invite link lands on the dashboard. The bot redeemed the invite while it
// set the channel up, and the sign-in redirect says how that went. This shows the answer once, as a toast.
import { onMounted } from 'vue';
import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';

import { takeInviteOutcome } from '@/api/auth';

// The invite page writes what the invite gives before it leaves for Twitch, because a used invite cannot be
// read again afterwards. The sign-in comes back to this same tab, so the tab's storage still holds it.
const giftKey = 'nnz.invite.gift';

export function rememberInviteGift(gift: string): void {
  sessionStorage.setItem(giftKey, gift);
}

function takeInviteGift(): string | null {
  const gift = sessionStorage.getItem(giftKey);

  sessionStorage.removeItem(giftKey);

  return gift;
}

export function useInviteOutcome(): void {
  const { t } = useI18n();

  onMounted(() => {
    const outcome = takeInviteOutcome();
    const gift = takeInviteGift();

    if (outcome === null) return;

    if (!outcome.redeemed) {
      toast.error(t('invite.outcome.failed'), { description: outcome.reason });

      return;
    }

    toast.success(t('invite.outcome.redeemed'), {
      description: gift === null
        ? t('invite.outcome.nowYours')
        : t('invite.outcome.nowYoursNamed', { gift }),
    });
  });
}
