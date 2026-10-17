<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The channel's currency: its name, the balance a new viewer starts with, and the cap. A channel with no
// currency yet gets the same form, and saving it sets the currency up.
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import { useCurrencyConfig, useSaveCurrencyConfig, type CurrencyConfig } from '@/api/economy';
import PanelCard, { type PanelState } from '@/components/layout/PanelCard.vue';
import SettingRow from '@/components/layout/SettingRow.vue';
import { Button } from '@/components/ui/button';
import { Field, FieldDescription, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { errorText, fieldErrorLines } from '@/lib/errors';

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const currency = useCurrencyConfig(channelId);
const save = useSaveCurrencyConfig(channelId);

const enabled = ref(false);
const name = ref('');
const plural = ref('');
const starting = ref(0);
const max = ref<number | ''>('');
const decimals = ref(0);
const error = ref<string | undefined>(undefined);

function fill(row: CurrencyConfig | null | undefined): void {
  enabled.value = row?.isEnabled ?? false;
  name.value = row?.currencyName ?? '';
  plural.value = row?.currencyNamePlural ?? '';
  starting.value = Number(row?.startingBalance ?? 0);

  max.value =
    row?.maxBalance === null || row?.maxBalance === undefined
      ? ''
      : Number(row.maxBalance);

  decimals.value = Number(row?.decimalPlaces ?? 0);
}

watch(() => currency.data.value, fill, { immediate: true });

const state = computed<PanelState>(() => {
  if (currency.isError.value)
    return 'error';

  return currency.isPending.value
    ? 'loading'
    : 'ready';
});

async function submit(): Promise<void> {
  if (name.value.trim() === '') {
    error.value = t('dashboard.economy.currency.nameRequired');

    return;
  }

  error.value = undefined;

  try {
    await save.mutateAsync({
      currencyName: name.value.trim(),
      currencyNamePlural: plural.value.trim() === ''
        ? null
        : plural.value.trim(),
      isEnabled: enabled.value,
      startingBalance: starting.value,
      maxBalance: max.value === ''
        ? null
        : max.value,
      decimalPlaces: decimals.value,
    });
  } catch (failure) {
    error.value = [errorText(failure, t), ...fieldErrorLines(failure)].join(' ');
  }
}
</script>

<template>
  <PanelCard
    :title="$t('dashboard.economy.currency.title')"
    :description="$t('dashboard.economy.currency.description')"
    :state="state"
    :error-text="$t('dashboard.economy.currency.error')"
    pad
    class="max-w-2xl"
    @retry="currency.refetch()"
  >
    <form class="flex flex-col gap-4" data-currency-form @submit.prevent="submit">
      <SettingRow
        v-model="enabled"
        :title="$t('dashboard.economy.currency.enabled')"
        :description="$t('dashboard.economy.currency.enabledHelp')"
      />
      <div class="grid gap-4 sm:grid-cols-2">
        <Field>
          <FieldLabel for="currency-name">{{ $t('dashboard.economy.currency.name') }}</FieldLabel>
          <Input
            id="currency-name"
            v-model="name"
            autocomplete="off"
            :aria-label="$t('dashboard.economy.currency.name')"
          />
        </Field>
        <Field>
          <FieldLabel for="currency-plural">{{
            $t('dashboard.economy.currency.plural')
          }}</FieldLabel>
          <Input
            id="currency-plural"
            v-model="plural"
            autocomplete="off"
            :aria-label="$t('dashboard.economy.currency.plural')"
          />
        </Field>
        <Field>
          <FieldLabel for="currency-starting">{{
            $t('dashboard.economy.currency.starting')
          }}</FieldLabel>
          <Input
            id="currency-starting"
            v-model.number="starting"
            type="number"
            min="0"
            step="1"
            :aria-label="$t('dashboard.economy.currency.starting')"
          />
        </Field>
        <Field>
          <FieldLabel for="currency-max">{{ $t('dashboard.economy.currency.max') }}</FieldLabel>
          <Input
            id="currency-max"
            v-model.number="max"
            type="number"
            min="0"
            step="1"
            :aria-label="$t('dashboard.economy.currency.max')"
          />
          <FieldDescription>{{ $t('dashboard.economy.currency.maxHelp') }}</FieldDescription>
        </Field>
        <Field>
          <FieldLabel for="currency-decimals">{{
            $t('dashboard.economy.currency.decimals')
          }}</FieldLabel>
          <Input
            id="currency-decimals"
            v-model.number="decimals"
            type="number"
            min="0"
            max="4"
            step="1"
            :aria-label="$t('dashboard.economy.currency.decimals')"
          />
        </Field>
      </div>
      <p v-if="error" role="alert" class="text-sm text-destructive">{{ error }}</p>
      <Button type="submit" size="sm" class="self-start" :disabled="save.isPending.value">
        {{ $t('dashboard.economy.currency.save') }}
      </Button>
    </form>
  </PanelCard>
</template>
