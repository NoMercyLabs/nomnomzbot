<!--
  -----------------------------------------------------------------------------
   Copyright (c) NoMercy Labs.

   This file is part of NomNomzBot, free software licensed under the GNU Affero
   General Public License v3.0 or later. You may redistribute and/or modify it
   under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

   SPDX-License-Identifier: AGPL-3.0-or-later
  -----------------------------------------------------------------------------
-->
<script setup lang="ts">
// The platform trust and safety desk: review the automatic account actions, see accounts flagged across
// channels, and apply or lift a network-wide block. The bot audits every call with the operator's reason,
// so nothing loads or changes until one is stated. Other operators' changes arrive over the admin hub.
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  useApplyNetworkBlock,
  useBlockPreview,
  useCrossTenantSignals,
  useLiftNetworkBlock,
  useNetworkBlocks,
  useReviewDecision,
  useReviewQueue,
  REVIEW_TAKE,
  type NetworkBlock,
  type PreviewParams,
  type TrustSafetyReviewItem,
} from '@/api/adminTrustSafety';
import AdminPager from './components/AdminPager.vue';
import TrustBlockRow from './components/TrustBlockRow.vue';
import TrustReviewRow from './components/TrustReviewRow.vue';
import TrustSignalRow from './components/TrustSignalRow.vue';
import FormDialog from '@/components/layout/FormDialog.vue';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { usePanelState } from '@/composables/usePanelState';
import { usePlatformCan } from '@/composables/usePlatformCan';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';

type Step =
  | { kind: 'confirm' | 'overturn'; item: TrustSafetyReviewItem } |
  { kind: 'lift'; block: NetworkBlock } |
  { kind: 'apply' };

const { t } = useI18n();
const { can } = usePlatformCan();

const justification = ref('');
const targetId = ref('');
const reason = ref('');
const previewParams = ref<PreviewParams | null>(null);
const step = ref<Step | null>(null);
const failed = ref(false);

const canReview = computed(() => can('trust-safety:review'));
const canManageBlocks = computed(() => can('network:block:manage'));

const queuePage = ref(1);
const queue = useReviewQueue(justification, canReview, queuePage);
const queueRows = computed<TrustSafetyReviewItem[]>(() => queue.data.value?.data ?? []);
const queueTotal = computed(() => Number(queue.data.value?.total ?? 0));
const queuePages = computed(() => Math.max(1, Math.ceil(queueTotal.value / REVIEW_TAKE)));

// A new reason is a new read, so the queue starts again at its first page.
watch(justification, () => {
  queuePage.value = 1;
});

const signals = useCrossTenantSignals(justification, canReview);
const blocks = useNetworkBlocks(justification, canManageBlocks);
const preview = useBlockPreview(previewParams);

// A preview only counts for the exact target and reason it was run with; an edit afterwards drops it.
const previewCurrent = computed(
  () =>
    previewParams.value?.targetTwitchUserId === targetId.value.trim() &&
    previewParams.value.justification === justification.value,
);
const shownPreview = computed(() =>
  previewCurrent.value
    ? preview.data.value
    : null);
const previewFailed = computed(() => previewCurrent.value && preview.isError.value);
const tenantNames = computed(() => {
  const tenants = shownPreview.value?.tenants ?? [];

  return tenants.map((tenant) => tenant.channelName).join(', ');
});

const confirmReview = useReviewDecision('confirm');
const overturnReview = useReviewDecision('overturn');
const lift = useLiftNetworkBlock();
const apply = useApplyNetworkBlock();

const queueState = usePanelState(queue, (page) =>
  (page.data?.length ?? 0) === 0);
const signalState = usePanelState(signals, (rows) =>
  rows.length === 0);
const blockState = usePanelState(blocks, (rows) =>
  rows.length === 0);

const previewCount = computed(() => Number(shownPreview.value?.tenantCount ?? 0));
const pending = computed(
  () =>
    confirmReview.isPending.value ||
    overturnReview.isPending.value ||
    lift.isPending.value ||
    apply.isPending.value,
);

const titleKeys = {
  confirm: 'review.confirmTitle',
  overturn: 'review.overturnTitle',
  lift: 'blocks.liftTitle',
  apply: 'apply.dialogTitle',
} as const satisfies Record<Step['kind'], string>;

const actionKeys = {
  confirm: 'review.confirm',
  overturn: 'review.overturn',
  lift: 'blocks.liftAction',
  apply: 'apply.action',
} as const satisfies Record<Step['kind'], string>;

const dialogTitle = computed<string>(() =>
  step.value
    ? t(`dashboard.admin.trustSafety.${titleKeys[step.value.kind]}`)
    : '',
);

const dialogAction = computed<string>(() =>
  step.value
    ? t(`dashboard.admin.trustSafety.${actionKeys[step.value.kind]}`)
    : '',
);

// The consequence is shown before the operator confirms, built from what the bot says it will touch.
const consequence = computed<string>(() => {
  const current = step.value;

  if (current?.kind === 'overturn')
    return t('dashboard.admin.trustSafety.review.overturnConsequence', {
      preview: current.item.reversalPreview,
    });

  if (current?.kind === 'confirm')
    return t('dashboard.admin.trustSafety.review.confirmConsequence');

  if (current?.kind === 'lift')
    return t('dashboard.admin.trustSafety.blocks.liftConsequence', {
      count: Number(current.block.channelCount),
    });

  return t('dashboard.admin.trustSafety.apply.consequence', { count: previewCount.value });
});

function startPreview(): void {
  previewParams.value = {
    targetTwitchUserId: targetId.value.trim(),
    justification: justification.value,
  };
}

function close(): void {
  step.value = null;
  failed.value = false;
}

async function submit(): Promise<void> {
  const current = step.value;

  if (!current) return;

  try {
    await run(current);
    close();
  } catch {
    failed.value = true;
  }
}

function run(current: Step): Promise<void> {
  const reason_ = justification.value;

  if (current.kind === 'confirm')
    return confirmReview.mutateAsync({
      detectionId: current.item.detectionId,
      justification: reason_,
    });

  if (current.kind === 'overturn')
    return overturnReview.mutateAsync({
      detectionId: current.item.detectionId,
      justification: reason_,
    });

  if (current.kind === 'lift')
    return lift.mutateAsync({ blockId: current.block.id, justification: reason_ });

  return apply.mutateAsync({
    targetTwitchUserId: targetId.value.trim(),
    reason: reason.value,
    justification: reason_,
    confirmedTenantCount: previewCount.value,
  });
}
</script>

<template>
  <PageHeader
    :title="$t('dashboard.admin.trustSafety.title')"
    :crumb="$t('dashboard.admin.crumb')"
  />
  <PageBody data-page-body>
    <div data-page-inset class="grid gap-1.5">
      <Label for="ts-justification">{{
        $t("dashboard.admin.trustSafety.justification.label")
      }}</Label>
      <Input
        id="ts-justification"
        v-model="justification"
        data-justification
        :placeholder="$t('dashboard.admin.trustSafety.justification.placeholder')"
      />
      <p class="text-xs text-muted-foreground">
        {{ $t("dashboard.admin.trustSafety.justification.hint") }}
      </p>
    </div>

    <div data-trust-grid class="grid min-w-0 gap-3 lg:grid-cols-2">
      <PanelCard
        v-if="canReview"
        tier="lead"
        :title="$t('dashboard.admin.trustSafety.review.title')"
        :description="$t('dashboard.admin.trustSafety.review.description')"
        :state="justification.trim() ? queueState : 'empty'"
        :empty-text="
          justification.trim()
            ? $t('dashboard.admin.trustSafety.review.empty')
            : $t('dashboard.admin.trustSafety.idle')
        "
        :error-text="$t('dashboard.admin.trustSafety.error')"
        @retry="queue.refetch()"
      >
        <ul class="divide-y divide-border">
          <TrustReviewRow
            v-for="item in queueRows"
            :key="item.detectionId"
            :item="item"
            :can-review="canReview"
            @confirm="step = { kind: 'confirm', item }"
            @overturn="step = { kind: 'overturn', item }"
          />
        </ul>
        <template v-if="queuePages > 1" #footer>
          <AdminPager
            :page="queuePage"
            :pages="queuePages"
            @previous="queuePage -= 1"
            @next="queuePage += 1"
          />
        </template>
      </PanelCard>

      <div class="flex min-w-0 flex-col gap-3">
        <PanelCard
          v-if="canReview"
          :title="$t('dashboard.admin.trustSafety.signals.title')"
          :description="$t('dashboard.admin.trustSafety.signals.description')"
          :state="justification.trim() ? signalState : 'empty'"
          :empty-text="
            justification.trim()
              ? $t('dashboard.admin.trustSafety.signals.empty')
              : $t('dashboard.admin.trustSafety.idle')
          "
          :error-text="$t('dashboard.admin.trustSafety.error')"
          @retry="signals.refetch()"
        >
          <ul class="divide-y divide-border">
            <TrustSignalRow
              v-for="signal in signals.data.value ?? []"
              :key="signal.provider + signal.subjectPlatformUserId"
              :signal="signal"
            />
          </ul>
        </PanelCard>

        <PanelCard
          v-if="canManageBlocks"
          :title="$t('dashboard.admin.trustSafety.blocks.title')"
          :description="$t('dashboard.admin.trustSafety.blocks.description')"
          :state="justification.trim() ? blockState : 'empty'"
          :empty-text="
            justification.trim()
              ? $t('dashboard.admin.trustSafety.blocks.empty')
              : $t('dashboard.admin.trustSafety.idle')
          "
          :error-text="$t('dashboard.admin.trustSafety.error')"
          @retry="blocks.refetch()"
        >
          <ul class="divide-y divide-border">
            <TrustBlockRow
              v-for="block in blocks.data.value ?? []"
              :key="block.id"
              :block="block"
              :can-lift="canManageBlocks"
              @lift="step = { kind: 'lift', block }"
            />
          </ul>
          <template #footer>
            <div class="grid w-full gap-2">
              <p class="text-sm font-medium">{{ $t("dashboard.admin.trustSafety.apply.title") }}</p>
              <div class="grid gap-2 sm:grid-cols-[minmax(0,1fr)_minmax(0,2fr)]">
                <div class="grid gap-1.5">
                  <Label for="ts-block-target">{{
                    $t("dashboard.admin.trustSafety.apply.target")
                  }}</Label>
                  <Input id="ts-block-target" v-model="targetId" data-block-target />
                </div>
                <div class="grid gap-1.5">
                  <Label for="ts-block-reason">{{
                    $t("dashboard.admin.trustSafety.apply.reasonLabel")
                  }}</Label>
                  <Input
                    id="ts-block-reason"
                    v-model="reason"
                    data-block-reason
                    :placeholder="$t('dashboard.admin.trustSafety.apply.reason')"
                  />
                </div>
              </div>
              <div class="flex flex-wrap gap-2">
                <Button
                  variant="outline"
                  :disabled="!justification.trim() || !targetId.trim()"
                  @click="startPreview"
                >
                  {{ $t("dashboard.admin.trustSafety.apply.preview") }}
                </Button>
                <Button v-if="shownPreview" variant="destructive" @click="step = { kind: 'apply' }">
                  {{ $t("dashboard.admin.trustSafety.apply.action") }}
                </Button>
              </div>
              <p
                v-if="previewFailed"
                data-preview-error
                role="alert"
                class="text-xs text-destructive"
              >
                {{ $t("dashboard.admin.trustSafety.apply.previewFailed") }}
              </p>
              <p v-if="shownPreview" class="text-xs text-muted-foreground">
                {{
                  $t("dashboard.admin.trustSafety.apply.previewResult", {
                    name: shownPreview.targetDisplayName ?? targetId,
                    count: previewCount,
                  })
                }}
                {{ tenantNames }}
              </p>
            </div>
          </template>
        </PanelCard>
      </div>
    </div>

    <FormDialog
      :open="step !== null"
      :title="dialogTitle"
      :description="consequence"
      :action="dialogAction"
      :pending="pending"
      :destructive="step?.kind === 'apply'"
      :error="failed ? $t('dashboard.admin.trustSafety.failed') : undefined"
      @update:open="close"
      @submit="submit"
    />
  </PageBody>
</template>
