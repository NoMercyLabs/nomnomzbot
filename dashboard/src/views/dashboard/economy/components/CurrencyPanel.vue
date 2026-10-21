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
import PanelCard from '@/components/layout/PanelCard.vue';
import type { PanelState } from '@/composables/usePanelState';
import SettingRow from '@/components/layout/SettingRow.vue';
import { Button } from '@/components/ui/button';
import { Field, FieldInfo, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { errorText, fieldErrorLines } from '@/lib/errors';

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const { can } = useCan();
const currency = useCurrencyConfig(channelId);
const save = useSaveCurrencyConfig(channelId);

const enabled = ref(false);
const name = ref('');
const plural = ref('');
const starting = ref(0);
const max = ref<number | ''>('');
const decimals = ref(0);
const iconUrl = ref('');
const error = ref<string | undefined>(undefined);

// The form shows what the server sent. A channel with no saved row gets the server's defaults, so the
// view keeps no default of its own.
function fill(row: CurrencyConfig | null | undefined): void {
  if (!row) return;

  enabled.value = row.isEnabled;
  name.value = row.currencyName;
  plural.value = row.currencyNamePlural ?? '';
  starting.value = Number(row.startingBalance);

  max.value = row.maxBalance === null || row.maxBalance === undefined
    ? ''
    : Number(row.maxBalance);

  decimals.value = Number(row.decimalPlaces);
  iconUrl.value = row.iconUrl ?? '';
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
      iconUrl: iconUrl.value.trim() === ''
        ? null
        : iconUrl.value.trim(),
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
        :readonly="!can('economy:config:write')"
        v-model="enabled"
        :title="$t('dashboard.economy.currency.enabled')"
        :description="$t('dashboard.economy.currency.enabledHelp')"
      />
      <div class="grid gap-4 sm:grid-cols-2">
        <Field>
          <FieldLabel for="currency-name">{{ $t("dashboard.economy.currency.name") }}</FieldLabel>
          <Input
            id="currency-name"
            v-model="name"
            autocomplete="off"
            :aria-label="$t('dashboard.economy.currency.name')"
          />
        </Field>
        <Field>
          <FieldLabel for="currency-plural">{{
            $t("dashboard.economy.currency.plural")
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
            $t("dashboard.economy.currency.starting")
          }}</FieldLabel>
          <Input
            id="currency-starting"
            v-model.number="starting"
            type="number"
            step="1"
            :aria-label="$t('dashboard.economy.currency.starting')"
          />
        </Field>
        <Field>
          <div data-field-hint-row class="-my-2 flex items-center gap-0">
            <FieldLabel for="currency-max">{{ $t("dashboard.economy.currency.max") }}</FieldLabel>
            <FieldInfo>{{ $t("dashboard.economy.currency.maxHelp") }}</FieldInfo>
          </div>
          <Input
            id="currency-max"
            v-model.number="max"
            type="number"
            step="1"
            :aria-label="$t('dashboard.economy.currency.max')"
          />
        </Field>
        <Field>
          <FieldLabel for="currency-decimals">{{
            $t("dashboard.economy.currency.decimals")
          }}</FieldLabel>
          <Input
            id="currency-decimals"
            v-model.number="decimals"
            type="number"
            step="1"
            :aria-label="$t('dashboard.economy.currency.decimals')"
          />
        </Field>
        <Field>
          <FieldLabel for="currency-icon">{{ $t("dashboard.economy.currency.icon") }}</FieldLabel>
          <Input
            id="currency-icon"
            v-model="iconUrl"
            type="url"
            autocomplete="off"
            :aria-label="$t('dashboard.economy.currency.icon')"
          />
        </Field>
      </div>
      <p v-if="error" role="alert" class="text-sm text-destructive">{{ error }}</p>
      <Button
        v-if="can('economy:config:write')"
        type="submit"
        size="sm"
        class="self-start"
        :disabled="save.isPending.value"
      >
        {{ $t("dashboard.economy.currency.save") }}
      </Button>
    </form>
  </PanelCard>
</template>
