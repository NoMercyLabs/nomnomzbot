<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Add or change an earning rule. A new rule picks a source that has no rule yet; an existing rule keeps
// its source, because the bot keeps one rule per source.
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import { useSaveEarningRule, type EarningRule } from '@/api/economy';
import FormDialog from '@/components/layout/FormDialog.vue';
import { Field, FieldDescription, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { errorText, fieldErrorLines } from '@/lib/errors';

const sources = [
  'ChatMessage',
  'WatchTime',
  'Follow',
  'Subscription',
  'GiftSubscription',
  'Cheer',
  'Raid',
  'Supporter',
] as const;

const props = defineProps<{
  rule: EarningRule | null;
  usedSources: readonly string[];
}>();
const open = defineModel<boolean>('open', { required: true });

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const save = useSaveEarningRule(channelId);

const freeSources = computed<string[]>(() =>
  sources.filter((source: string): boolean => !props.usedSources.includes(source)),
);

const source = ref('');
const rate = ref(1);
const window = ref<number | ''>('');
const error = ref<string | undefined>(undefined);

watch(
  open,
  (isOpen: boolean): void => {
    if (!isOpen) return;

    source.value = props.rule?.source ?? freeSources.value[0] ?? '';
    rate.value = Number(props.rule?.rate ?? 1);

    window.value =
      props.rule?.unitWindowSeconds === null || props.rule?.unitWindowSeconds === undefined
        ? ''
        : Number(props.rule.unitWindowSeconds);

    error.value = undefined;
  },
  { immediate: true },
);

async function submit(): Promise<void> {
  if (source.value === '') {
    error.value = t('dashboard.economy.rules.dialog.sourceRequired');

    return;
  }

  error.value = undefined;

  try {
    await save.mutateAsync({
      source: source.value,
      isEnabled: props.rule?.isEnabled ?? true,
      rate: rate.value,
      unitWindowSeconds: window.value === ''
        ? null
        : window.value,
      perWindowCap: props.rule?.perWindowCap ?? null,
      perStreamCap: props.rule?.perStreamCap ?? null,
      minRoleLevel: props.rule?.minRoleLevel ?? null,
      bonusConfig: props.rule?.bonusConfig ?? null,
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
      rule
        ? $t('dashboard.economy.rules.dialog.editTitle')
        : $t('dashboard.economy.rules.dialog.newTitle')
    "
    :description="$t('dashboard.economy.rules.dialog.description')"
    :action="$t('dashboard.economy.rules.dialog.save')"
    :pending="save.isPending.value"
    :error="error"
    @submit="submit"
  >
    <Field>
      <FieldLabel>{{ $t('dashboard.economy.rules.dialog.source') }}</FieldLabel>
      <Select v-model="source" :disabled="rule !== null">
        <SelectTrigger :aria-label="$t('dashboard.economy.rules.dialog.source')" class="w-full">
          <SelectValue :placeholder="$t('dashboard.economy.rules.dialog.sourceNone')" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem v-for="item in rule ? [rule.source] : freeSources" :key="item" :value="item">
            {{ $t(`dashboard.economy.sources.${item}`) }}
          </SelectItem>
        </SelectContent>
      </Select>
    </Field>
    <Field>
      <FieldLabel for="rule-rate">{{ $t('dashboard.economy.rules.dialog.rate') }}</FieldLabel>
      <Input
        id="rule-rate"
        v-model.number="rate"
        type="number"
        min="0"
        step="1"
        :aria-label="$t('dashboard.economy.rules.dialog.rate')"
      />
    </Field>
    <Field>
      <FieldLabel for="rule-window">{{ $t('dashboard.economy.rules.dialog.window') }}</FieldLabel>
      <Input
        id="rule-window"
        v-model.number="window"
        type="number"
        min="0"
        step="1"
        :aria-label="$t('dashboard.economy.rules.dialog.window')"
      />
      <FieldDescription>{{ $t('dashboard.economy.rules.dialog.windowHelp') }}</FieldDescription>
    </Field>
  </FormDialog>
</template>
