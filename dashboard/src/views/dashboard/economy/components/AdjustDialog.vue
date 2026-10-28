<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Change one viewer's balance: a signed amount (negative takes currency away) and the reason that is
// written to the wallet's ledger.
import { ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import { useAdjustAccount, type CurrencyAccount } from '@/api/economy';
import FormDialog from '@/components/layout/FormDialog.vue';
import { Field, FieldInfo, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { errorText, fieldErrorLines } from '@/lib/errors';

const props = defineProps<{ account: CurrencyAccount | null }>();
const open = defineModel<boolean>('open', { required: true });

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const adjust = useAdjustAccount(channelId);

const amount = ref<number | ''>('');
const reason = ref('');
const error = ref<string | undefined>(undefined);

watch(open, (isOpen: boolean): void => {
  if (!isOpen) return;

  amount.value = '';
  reason.value = '';
  error.value = undefined;
});

async function submit(): Promise<void> {
  if (props.account === null) return;

  if (
    amount.value === '' ||
    !Number.isInteger(amount.value) ||
    amount.value === 0 ||
    reason.value.trim() === ''
  ) {
    error.value = t('dashboard.economy.balances.dialog.invalid');

    return;
  }

  error.value = undefined;

  try {
    await adjust.mutateAsync({
      viewerUserId: props.account.viewerUserId,
      amount: amount.value,
      reason: reason.value.trim(),
    });
  } catch (failure) {
    error.value = [errorText(failure, t), ...fieldErrorLines(failure)].join(' ');

    return;
  }

  open.value = false;
}
</script>

<template>
  <FormDialog
    v-model:open="open"
    :title="
      $t('dashboard.economy.balances.dialog.title', { name: account?.viewerDisplayName ?? '' })
    "
    :description="
      $t('dashboard.economy.balances.dialog.description', {
        balance: Number(account?.balance ?? 0),
      })
    "
    :action="$t('dashboard.economy.balances.dialog.save')"
    :pending="adjust.isPending.value"
    :error="error"
    @submit="submit"
  >
    <Field>
      <div class="flex items-center gap-1.5">
        <FieldLabel for="adjust-amount">{{
          $t("dashboard.economy.balances.dialog.amount")
        }}</FieldLabel>
        <FieldInfo>{{ $t("dashboard.economy.balances.dialog.amountHelp") }}</FieldInfo>
      </div>
      <Input
        id="adjust-amount"
        v-model.number="amount"
        type="number"
        step="1"
        :aria-label="$t('dashboard.economy.balances.dialog.amount')"
      />
    </Field>
    <Field>
      <FieldLabel for="adjust-reason">{{
        $t("dashboard.economy.balances.dialog.reason")
      }}</FieldLabel>
      <Input
        id="adjust-reason"
        v-model="reason"
        autocomplete="off"
        :aria-label="$t('dashboard.economy.balances.dialog.reason')"
      />
    </Field>
  </FormDialog>
</template>
