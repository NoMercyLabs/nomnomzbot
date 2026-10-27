<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Where a simple reply goes: Twitch chat, the chat overlay, both, or nowhere, and whether the overlay line
// is HTML. Every editor with a simple reply uses this one field, so the choices and the hint read the same
// everywhere. All four targets sit in one segmented control and HTML is one switch: nothing here is the primary
// action, so nothing carries the accent. HTML only means something on the overlay, so the switch is off
// (and cleared) for the other targets.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import LabeledSwitch from '@/components/layout/LabeledSwitch.vue';
import {
  reachesOverlay,
  replyTargets,
  targetOf,
  type ReplyDelivery,
  type ReplyTarget,
} from '@/components/layout/replyMode';
import SegmentedControl from '@/components/layout/SegmentedControl.vue';
import { Field, FieldInfo, FieldLabel } from '@/components/ui/field';

const delivery = defineModel<ReplyDelivery>({ required: true });

// A locked editor shows the setting but lets no one change it.
const props = withDefaults(defineProps<{ disabled?: boolean }>(), { disabled: false });

const { t } = useI18n();

const options = computed(() =>
  replyTargets.map((value) => ({
    value,
    label: t(`common.replyDelivery.target.${value}`),
  })),
);

const target = computed<ReplyTarget>({
  get: () => targetOf(delivery.value),
  set: (value: ReplyTarget): void => {
    delivery.value = {
      target: value,
      html: reachesOverlay(value) && delivery.value.html,
    };
  },
});

const html = computed<boolean>({
  get: () => delivery.value.html,
  set: (value: boolean): void => {
    delivery.value = {
      target: target.value,
      html: value,
    };
  },
});

const htmlLocked = computed<boolean>(() => props.disabled || !reachesOverlay(target.value));
</script>

<template>
  <div data-reply-delivery class="grid gap-2">
    <Field>
      <div class="flex items-center gap-1.5">
        <FieldLabel>{{ $t('common.replyDelivery.label') }}</FieldLabel>
        <FieldInfo>{{ $t('common.replyDelivery.hint') }}</FieldInfo>
      </div>
      <SegmentedControl
        v-model="target"
        :label="$t('common.replyDelivery.label')"
        :options="options"
        :disabled="disabled"
      />
    </Field>
    <LabeledSwitch
      v-model="html"
      :label="$t('common.replyDelivery.html')"
      :help="$t('common.replyDelivery.htmlHelp')"
      :disabled="htmlLocked"
    />
  </div>
</template>
