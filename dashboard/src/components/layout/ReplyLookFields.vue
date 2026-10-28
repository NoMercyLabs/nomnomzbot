<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// How a reply looks on the chat overlay: the highlight color (a design token), the size, and how long the line
// stays. It sits in a collapsed "Look" section with a one-line summary, because most replies keep the default and
// nothing here is the primary action, so nothing carries the accent. ReplyDeliveryField shows it for the
// overlay targets only.
import ChevronDown from '~icons/nnz/chevron-down';
import { computed, useId } from 'vue';
import { useI18n } from 'vue-i18n';

import DurationInput from '@/components/layout/DurationInput.vue';
import OptionSelect from '@/components/layout/OptionSelect.vue';
import {
  maxLookSeconds,
  replyHighlights,
  replySizes,
  type ReplyHighlight,
  type ReplyLookView,
  type ReplySize,
} from '@/components/layout/replyMode';
import SegmentedControl from '@/components/layout/SegmentedControl.vue';
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from '@/components/ui/collapsible';
import { Field, FieldInfo, FieldLabel } from '@/components/ui/field';

const look = defineModel<ReplyLookView>({ required: true });

// A locked editor shows the look but lets no one change it.
withDefaults(defineProps<{ disabled?: boolean }>(), { disabled: false });

const { t } = useI18n();

const durationId = useId();

const highlightOptions = computed(() => [
  {
    value: '',
    label: t('common.replyDelivery.look.highlight.default'),
  },
  ...replyHighlights.map((value) => ({
    value,
    label: t(`common.replyDelivery.look.highlight.${value}`),
  })),
]);

const sizeOptions = computed(() =>
  replySizes.map((value) => ({
    value,
    label: t(`common.replyDelivery.look.size.${value}`),
  })),
);

const highlight = computed<string>({
  get: () => look.value.highlight ?? '',
  set: (value: string): void => {
    look.value = {
      ...look.value,
      highlight: replyHighlights.find((item) => item === value) ?? null,
    };
  },
});

const size = computed<ReplySize>({
  get: () => look.value.size,
  set: (value: ReplySize): void => {
    look.value = {
      ...look.value,
      size: value,
    };
  },
});

// The field counts whole seconds, 0 meaning "the widget's own show time". Text that is not a length (NaN) is not
// stored: the field keeps what the person typed and the look keeps its last good value.
const duration = computed<number>({
  get: () => look.value.durationSeconds ?? 0,
  set: (value: number): void => {
    if (Number.isNaN(value)) return;

    look.value = {
      ...look.value,
      durationSeconds: value === 0
        ? null
        : value,
    };
  },
});

function highlightLabel(value: ReplyHighlight): string {
  return t(`common.replyDelivery.look.highlight.${value}`);
}

// The closed section still says what the look is, so a person sees a custom look without opening it.
const summary = computed<string>(() => {
  const parts: string[] = [];

  if (look.value.highlight !== null) {
    parts.push(highlightLabel(look.value.highlight));
  }

  if (look.value.size !== 'normal') {
    parts.push(t(`common.replyDelivery.look.size.${look.value.size}`));
  }

  if (look.value.durationSeconds !== null) {
    parts.push(t('common.replyDelivery.look.seconds', { count: look.value.durationSeconds }));
  }

  return parts.length === 0
    ? t('common.replyDelivery.look.summaryDefault')
    : parts.join(', ');
});
</script>

<template>
  <Collapsible data-reply-look class="rounded-lg border border-border">
    <CollapsibleTrigger
      data-reply-look-trigger
      class="group flex w-full items-center justify-between gap-3 rounded-lg px-3 py-2 text-left text-sm"
    >
      <span class="font-medium">{{ $t('common.replyDelivery.look.label') }}</span>
      <span class="ml-auto truncate text-muted-foreground">{{ summary }}</span>
      <ChevronDown
        aria-hidden="true"
        class="size-4 shrink-0 text-muted-foreground transition-transform group-data-[state=open]:rotate-180 motion-reduce:transition-none"
      />
    </CollapsibleTrigger>
    <CollapsibleContent
      force-mount
      class="grid gap-4 border-t border-border p-3 data-[state=closed]:hidden"
    >
      <Field>
        <div class="flex items-center gap-1.5">
          <FieldLabel>{{ $t('common.replyDelivery.look.highlight.label') }}</FieldLabel>
          <FieldInfo>{{ $t('common.replyDelivery.look.highlight.help') }}</FieldInfo>
        </div>
        <OptionSelect
          v-model="highlight"
          data-reply-look-highlight
          :options="highlightOptions"
          :aria-label="$t('common.replyDelivery.look.highlight.label')"
          :disabled="disabled"
        />
      </Field>
      <Field>
        <FieldLabel>{{ $t('common.replyDelivery.look.size.label') }}</FieldLabel>
        <SegmentedControl
          v-model="size"
          :label="$t('common.replyDelivery.look.size.label')"
          :options="sizeOptions"
          :disabled="disabled"
        />
      </Field>
      <Field>
        <div class="flex items-center gap-1.5">
          <FieldLabel :for="durationId">{{ $t('common.replyDelivery.look.duration.label') }}</FieldLabel>
          <FieldInfo>{{ $t('common.replyDelivery.look.duration.help') }}</FieldInfo>
        </div>
        <DurationInput
          :id="durationId"
          v-model="duration"
          :min="0"
          :max="maxLookSeconds"
          :aria-label="$t('common.replyDelivery.look.duration.label')"
          :disabled="disabled"
        />
      </Field>
    </CollapsibleContent>
  </Collapsible>
</template>
