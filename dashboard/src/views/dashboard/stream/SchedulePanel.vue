<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The Twitch schedule: the vacation window on top, the segments below. Add segment is the one primary action;
// Cancel, Restore and Edit on a row stay quiet. The vacation has its own Save, so a typo never reaches Twitch.
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  useSaveScheduleSettings,
  useSchedule,
  useUpdateSegment,
  type Schedule,
  type ScheduleSegment,
  type ScheduleSettings,
} from '@/api/schedule';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SettingRow from '@/components/layout/SettingRow.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';
import { useTime } from '@/composables/useTime';
import { fromLocalInput, toLocalInput } from '@/lib/time';

import SegmentDialog from './SegmentDialog.vue';

const { t } = useI18n();
const time = useTime();
const { channelId } = useCurrentChannel();
const { can } = useCan();
const canEdit = computed(() => can('live-ops:schedule:write'));
const query = useSchedule(channelId);
const saveSettings = useSaveScheduleSettings(channelId);
const updateSegment = useUpdateSegment(channelId);

const state = usePanelState(query);
const schedule = computed<Schedule | null>(() => query.data.value ?? null);
const segments = computed<ScheduleSegment[]>(() => schedule.value?.segments ?? []);

const vacationOn = ref(false);
const vacationStart = ref('');
const vacationEnd = ref('');
const dialogOpen = ref(false);
const editing = ref<ScheduleSegment | null>(null);

watch(schedule, (current: Schedule | null) => {
  vacationOn.value = current?.vacation != null;
  vacationStart.value = toLocalInput(current?.vacation?.startTime);
  vacationEnd.value = toLocalInput(current?.vacation?.endTime);
}, { immediate: true });

const vacationValid = computed(
  () => !vacationOn.value || (vacationStart.value !== '' && vacationEnd.value > vacationStart.value),
);

const when = (iso: string): string => time.dateTime(iso);

const isCanceled = (segment: ScheduleSegment): boolean => segment.canceledUntil != null;

function saveVacation(): void {
  const body: ScheduleSettings = vacationOn.value
    ? {
        isVacationEnabled: true,
        vacationStartTime: fromLocalInput(vacationStart.value),
        vacationEndTime: fromLocalInput(vacationEnd.value),
      }
    : { isVacationEnabled: false };

  saveSettings.mutate(body);
}

function addSegment(): void {
  editing.value = null;
  dialogOpen.value = true;
}

function editSegment(segment: ScheduleSegment): void {
  editing.value = segment;
  dialogOpen.value = true;
}

function toggleCanceled(segment: ScheduleSegment): void {
  updateSegment.mutate({ segmentId: segment.id, change: { isCanceled: !isCanceled(segment) } });
}
</script>

<template>
  <PanelCard
    :title="t('dashboard.stream.schedule.title')"
    :description="t('dashboard.stream.schedule.description')"
    tier="lead"
    :state="state"
    :error-text="t('dashboard.stream.schedule.error')"
    @retry="query.refetch()"
  >
    <div class="flex flex-col gap-6">
      <div v-if="canEdit" class="flex flex-col gap-2">
        <span class="text-sm font-medium">{{ t('dashboard.stream.schedule.vacation') }}</span>
        <RowGroup>
          <SettingRow
            v-model="vacationOn"
            :title="t('dashboard.stream.schedule.isVacationEnabled')"
            :description="t('dashboard.stream.schedule.vacationHint')"
          />
        </RowGroup>
        <div v-if="vacationOn" class="grid gap-3 sm:grid-cols-2">
          <div class="grid gap-1.5">
            <Label for="vacation-start">{{ t('dashboard.stream.schedule.vacationStartTime') }}</Label>
            <Input id="vacation-start" v-model="vacationStart" type="datetime-local" />
          </div>
          <div class="grid gap-1.5">
            <Label for="vacation-end">{{ t('dashboard.stream.schedule.vacationEndTime') }}</Label>
            <Input id="vacation-end" v-model="vacationEnd" type="datetime-local" />
          </div>
        </div>
        <div class="flex justify-end">
          <Button
            variant="outline"
            data-vacation-save
            :disabled="!vacationValid || saveSettings.isPending.value"
            @click="saveVacation"
          >
            {{ t('dashboard.stream.schedule.saveVacation') }}
          </Button>
        </div>
      </div>

      <div class="flex flex-col gap-2">
        <div class="flex items-center justify-between gap-2">
          <span class="text-sm font-medium">{{ t('dashboard.stream.schedule.segments') }}</span>
          <Button v-if="canEdit" data-segment-add @click="addSegment">
            {{ t('dashboard.stream.schedule.addSegment') }}
          </Button>
        </div>
        <p v-if="segments.length === 0" class="text-sm text-muted-foreground">
          {{ t('dashboard.stream.schedule.none') }}
        </p>
        <RowGroup v-else>
          <div
            v-for="segment in segments"
            :key="segment.id"
            data-segment-row
            class="flex min-h-11 flex-wrap items-center gap-3 rounded-[8px] px-3 py-2"
          >
            <span class="min-w-0 flex-1">
              <span class="block text-sm font-medium wrap-break-word">{{
                segment.title || segment.category?.name || t('dashboard.stream.schedule.untitled')
              }}</span>
              <span class="block text-sm text-muted-foreground">
                {{
                  segment.category
                    ? t('dashboard.stream.schedule.whenIn', { when: when(segment.startTime), category: segment.category.name })
                    : when(segment.startTime)
                }}
              </span>
            </span>
            <StatusBadge v-if="segment.isRecurring" tone="off" :label="t('dashboard.stream.schedule.weekly')" />
            <StatusBadge v-if="isCanceled(segment)" tone="off" :label="t('dashboard.stream.schedule.isCanceled')" />
            <Button v-if="canEdit"
                    variant="ghost"
                    size="sm"
                    data-segment-edit
                    @click="editSegment(segment)"
            >
              {{ t('dashboard.stream.schedule.edit') }}
            </Button>
            <Button
              v-if="canEdit"
              variant="ghost"
              size="sm"
              data-segment-cancel
              :disabled="updateSegment.isPending.value"
              @click="toggleCanceled(segment)"
            >
              {{ isCanceled(segment) ? t('dashboard.stream.schedule.restore') : t('dashboard.stream.schedule.cancel') }}
            </Button>
          </div>
        </RowGroup>
      </div>
    </div>
  </PanelCard>
  <SegmentDialog v-if="canEdit" v-model:open="dialogOpen" :segment="editing" />
</template>
