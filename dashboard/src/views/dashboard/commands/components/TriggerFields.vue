<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// How chat triggers a command: the prefix (channel default, its own, or none) and how the message is matched.
// The custom prefix shows only for Custom and the pattern only for Regex. Each has a slot of fixed height,
// so choosing a mode never moves what sits below.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import SegmentedControl from '@/components/layout/SegmentedControl.vue';
import { Field, FieldInfo, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';

import { matchModes, prefixModes, type MatchMode, type PrefixMode } from './commandDraft';

const prefixMode = defineModel<PrefixMode>('prefixMode', { required: true });
const customPrefix = defineModel<string>('customPrefix', { required: true });
const matchMode = defineModel<MatchMode>('matchMode', { required: true });
const matchPattern = defineModel<string>('matchPattern', { required: true });

const { t } = useI18n();

const prefixOptions = computed<{ value: PrefixMode; label: string }[]>(() =>
  prefixModes.map((mode) => ({ value: mode, label: t(`dashboard.commands.trigger.prefix.${mode}`) })),
);
const matchOptions = computed<{ value: MatchMode; label: string }[]>(() =>
  matchModes.map((mode) => ({ value: mode, label: t(`dashboard.commands.trigger.match.${mode}`) })),
);
</script>

<template>
  <div data-trigger-fields class="grid gap-4">
    <Field>
      <div class="flex items-center gap-1.5">
        <FieldLabel>{{ $t('dashboard.commands.trigger.prefixLabel') }}</FieldLabel>
        <FieldInfo>{{ $t('dashboard.commands.trigger.prefixHelp') }}</FieldInfo>
      </div>
      <SegmentedControl
        v-model="prefixMode"
        :label="$t('dashboard.commands.trigger.prefixLabel')"
        :options="prefixOptions"
      />
      <div class="min-h-9">
        <Input
          v-if="prefixMode === 'Custom'"
          v-model="customPrefix"
          autocomplete="off"
          :aria-label="$t('dashboard.commands.trigger.customPrefix')"
          :aria-invalid="customPrefix === ''"
          :placeholder="$t('dashboard.commands.trigger.customPrefix')"
        />
      </div>
    </Field>
    <Field>
      <div class="flex items-center gap-1.5">
        <FieldLabel>{{ $t('dashboard.commands.trigger.matchLabel') }}</FieldLabel>
        <FieldInfo>{{ $t('dashboard.commands.trigger.matchHelp') }}</FieldInfo>
      </div>
      <SegmentedControl
        v-model="matchMode"
        :label="$t('dashboard.commands.trigger.matchLabel')"
        :options="matchOptions"
      />
      <div class="min-h-9">
        <Input
          v-if="matchMode === 'Regex'"
          v-model="matchPattern"
          autocomplete="off"
          :aria-label="$t('dashboard.commands.trigger.matchPattern')"
          :aria-invalid="matchPattern === ''"
          :placeholder="$t('dashboard.commands.trigger.matchPattern')"
        />
      </div>
    </Field>
  </div>
</template>
