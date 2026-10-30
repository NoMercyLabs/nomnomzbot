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

import { computed, ref } from 'vue';

import { useBillingTiers } from '@/api/adminBilling';

// The server takes 1 to 120 months for a plan that ends.
export const MIN_MONTHS = 1;
export const MAX_MONTHS = 120;

// One tier (any tier, a hidden one too) and one length, whole months or forever: the state behind a
// gift and an invite. TierLengthFields draws it; the owner reads the length it sends.
export function useTierLength() {
  const tiers = useBillingTiers();
  const tierId = ref('');
  const months = ref(String(MIN_MONTHS));
  const forever = ref(false);

  const tierChoices = computed(() =>
    (tiers.data.value ?? []).map((tier) => ({ value: tier.id, label: tier.displayName })));
  const monthCount = computed<number>(() => Number(months.value));
  const lengthValid = computed<boolean>(() => {
    const count = monthCount.value;

    return forever.value || (Number.isInteger(count) && count >= MIN_MONTHS && count <= MAX_MONTHS);
  });

  function tierLabel(id: string | null | undefined): string {
    return (tiers.data.value ?? []).find((tier) => tier.id === id)?.displayName ?? '';
  }

  function reset(): void {
    tierId.value = '';
    months.value = String(MIN_MONTHS);
    forever.value = false;
  }

  return {
    tiers,
    tierId,
    months,
    forever,
    tierChoices,
    monthCount,
    lengthValid,
    tierLabel,
    reset,
  };
}
