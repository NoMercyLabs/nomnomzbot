<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// One schedule segment: adds a new one, or changes the one it is given. A change sends only what moved.
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  useCreateSegment,
  useUpdateSegment,
  type NewSegment,
  type ScheduleSegment,
  type SegmentChange,
} from '@/api/schedule';
import type { Category } from '@/api/stream';
import FormDialog from '@/components/layout/FormDialog.vue';
import LabeledSwitch from '@/components/layout/LabeledSwitch.vue';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { fromLocalInput, localTimeZone, minutesBetween, toLocalInput } from '@/lib/time';

import CategoryPicker from './CategoryPicker.vue';

const DEFAULT_MINUTES = 120;

const props = defineProps<{ segment: ScheduleSegment | null }>();
const open = defineModel<boolean>('open', { required: true });

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const create = useCreateSegment(channelId);
const update = useUpdateSegment(channelId);

const startTime = ref('');
const duration = ref(String(DEFAULT_MINUTES));
const title = ref('');
const gameName = ref('');
const categoryId = ref<string | null>(null);
const isRecurring = ref(false);

const editing = computed(() => props.segment !== null);
const pending = computed(() => create.isPending.value || update.isPending.value);
const valid = computed(
  () => startTime.value !== '' && Number.isInteger(Number(duration.value)) && Number(duration.value) >= 30,
);
const zone = localTimeZone();

watch(open, (isOpen: boolean) => {
  if (!isOpen) return;

  const segment = props.segment;

  startTime.value = toLocalInput(segment?.startTime);

  duration.value = segment
    ? String(minutesBetween(segment.startTime, segment.endTime))
    : String(DEFAULT_MINUTES);

  title.value = segment?.title ?? '';
  gameName.value = segment?.category?.name ?? '';
  categoryId.value = segment?.category?.id ?? null;
  isRecurring.value = segment?.isRecurring ?? false;
});

function pickCategory(category: Category): void {
  categoryId.value = category.id;
}

function close(): void {
  open.value = false;
}

function submit(): void {
  if (!valid.value) return;

  if (props.segment === null) {
    const body: NewSegment = {
      startTime: fromLocalInput(startTime.value),
      timezone: zone,
      duration: duration.value,
      isRecurring: isRecurring.value,
      categoryId: categoryId.value,
      title: title.value.trim(),
    };

    create.mutate(body, { onSuccess: close });

    return;
  }

  const change: SegmentChange = {
    startTime: fromLocalInput(startTime.value),
    duration: duration.value,
    categoryId: categoryId.value,
    title: title.value.trim(),
    timezone: zone,
  };

  update.mutate({ segmentId: props.segment.id, change }, { onSuccess: close });
}
</script>

<template>
  <FormDialog
    v-model:open="open"
    :title="editing ? t('dashboard.stream.schedule.editSegment') : t('dashboard.stream.schedule.addSegment')"
    :description="t('dashboard.stream.schedule.segmentHint')"
    :action="t('dashboard.stream.schedule.saveSegment')"
    :pending="pending"
    @submit="submit"
  >
    <div class="grid gap-1.5">
      <Label for="segment-start">{{ t('dashboard.stream.schedule.startTime') }}</Label>
      <Input id="segment-start" v-model="startTime" type="datetime-local" />
    </div>
    <div class="grid gap-1.5">
      <Label for="segment-duration">{{ t('dashboard.stream.schedule.duration') }}</Label>
      <Input id="segment-duration"
             v-model="duration"
             type="number"
             min="30"
             max="1440"
      />
    </div>
    <div class="grid gap-1.5">
      <Label for="segment-title">{{ t('dashboard.stream.schedule.segmentTitle') }}</Label>
      <Input id="segment-title" v-model="title" maxlength="140" />
    </div>
    <div class="grid gap-1.5">
      <Label>{{ t('dashboard.stream.schedule.categoryId') }}</Label>
      <CategoryPicker
        v-model="gameName"
        :channel-id="channelId"
        :recent="[]"
        @picked="pickCategory"
      />
    </div>
    <LabeledSwitch
      v-if="!editing"
      v-model="isRecurring"
      :label="t('dashboard.stream.schedule.isRecurring')"
    />
  </FormDialog>
</template>
