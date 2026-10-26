<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The reports viewers filed about a chatter. A moderator reads the open queue and dismisses a report or escalates
// it. Both buttons are outline: neither is the page's main action, and a report can be worked in either order.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';

import {
  REPORT_STATUSES,
  useReports,
  useResolveReport,
  type ReportAction,
  type ReportStatus,
  type ViewerReport,
} from '@/api/reports';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SegmentedControl from '@/components/layout/SegmentedControl.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';
import { useTime } from '@/composables/useTime';

const { t } = useI18n();
const { can } = useCan();
const { channelId } = useCurrentChannel();
const time = useTime();

const status = ref<ReportStatus>('open');
const reports = useReports(channelId, status);
const resolve = useResolveReport(channelId);
const state = usePanelState(reports, (list: ViewerReport[]) =>
  list.length === 0);

const options = computed(() =>
  REPORT_STATUSES.map((value) => ({ value, label: t(`dashboard.settings.reports.status.${value}`) })));

function who(report: ViewerReport): string {
  return report.reportedUsername ?? report.reportedTwitchUserId;
}

function detailOf(report: ViewerReport): string {
  const filed = t('dashboard.settings.reports.filed', {
    name: report.reporterName ?? t('dashboard.settings.reports.someone'),
    when: time.relative(report.createdAt),
  });

  return `${report.reason} · ${filed}`;
}

function work(report: ViewerReport, action: ReportAction): void {
  resolve.mutate({ report, action }, {
    onSuccess: () =>
      toast.success(t(`dashboard.settings.reports.done.${action}`, { name: who(report) })),
    onError: () => toast.error(t('dashboard.settings.reports.error', { name: who(report) })),
  });
}
</script>

<template>
  <PageHeader
    :title="$t('frame.nav.settingsPages.reports')"
    :crumb="$t('dashboard.settings.reports.crumb')"
  />
  <PageBody>
    <p class="text-sm text-muted-foreground">{{ $t("dashboard.settings.reports.explain") }}</p>
    <PanelCard
      tier="lead"
      :title="$t('dashboard.settings.reports.listTitle')"
      :state="state"
      :empty-text="$t(`dashboard.settings.reports.empty.${status}`)"
      :error-text="$t('dashboard.settings.reports.loadError')"
      @retry="reports.refetch()"
    >
      <template #actions>
        <SegmentedControl
          v-model="status"
          :label="$t('dashboard.settings.reports.statusLabel')"
          :options="options"
        />
      </template>
      <RowGroup>
        <SummaryRow
          v-for="report in reports.data.value"
          :key="report.id"
          :data-report="report.id"
          :title="who(report)"
          :detail="detailOf(report)"
        >
          <template v-if="can('moderation:report:triage') && report.status === 'open'" #trailing>
            <Button
              variant="outline"
              size="sm"
              :data-report-dismiss="report.id"
              :disabled="resolve.isPending.value"
              @click="work(report, 'dismiss')"
            >
              {{ $t("dashboard.settings.reports.dismiss") }}
            </Button>
            <Button
              variant="outline"
              size="sm"
              :data-report-escalate="report.id"
              :disabled="resolve.isPending.value"
              @click="work(report, 'escalate')"
            >
              {{ $t("dashboard.settings.reports.escalate") }}
            </Button>
          </template>
        </SummaryRow>
      </RowGroup>
    </PanelCard>
  </PageBody>
</template>
