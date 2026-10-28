<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Where a simple reply goes: Twitch chat, the chat overlay, both, or nowhere, whether the overlay line
// is HTML, and (for the overlay targets) how the line looks. Every editor with a simple reply uses this one field, so the choices and the hint read the same
// everywhere. All four targets sit in one segmented control and HTML is one switch: nothing here is the primary
// action, so nothing carries the accent. HTML only means something on the overlay, so the switch is off
// (and cleared) for the other targets.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import LabeledSwitch from '@/components/layout/LabeledSwitch.vue';
import ReplyLookFields from '@/components/layout/ReplyLookFields.vue';
import {
  lookOf,
  reachesOverlay,
  replyTargets,
  targetOf,
  withLook,
  type ReplyDelivery,
  type ReplyLookView,
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

// HTML and the look only mean something on the overlay, so a target that never reaches it clears both.
const target = computed<ReplyTarget>({
  get: () => targetOf(delivery.value),
  set: (value: ReplyTarget): void => {
    delivery.value = withLook(
      {
        target: value,
        html: reachesOverlay(value) && delivery.value.html,
      },
      lookOf(delivery.value),
    );
  },
});

const html = computed<boolean>({
  get: () => delivery.value.html,
  set: (value: boolean): void => {
    delivery.value = withLook(
      {
        target: target.value,
        html: value,
      },
      lookOf(delivery.value),
    );
  },
});

const look = computed<ReplyLookView>({
  get: () => lookOf(delivery.value),
  set: (value: ReplyLookView): void => {
    delivery.value = withLook(delivery.value, value);
  },
});

const overlay = computed<boolean>(() => reachesOverlay(target.value));

const htmlLocked = computed<boolean>(() => props.disabled || !overlay.value);
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
    <ReplyLookFields v-if="overlay" v-model="look" :disabled="disabled" />
  </div>
</template>
