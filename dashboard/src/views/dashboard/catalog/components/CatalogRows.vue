<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// SummaryRow, SettingRow and RowEditDelete in each of their states inside one RowGroup per kind.
import { ref } from 'vue';
import { useI18n } from 'vue-i18n';

import PanelCard from '@/components/layout/PanelCard.vue';
import ReadOnlyScope from '@/components/layout/ReadOnlyScope.vue';
import RowEditDelete from '@/components/layout/RowEditDelete.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SettingRow from '@/components/layout/SettingRow.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';

const { t } = useI18n();

const on = ref(true);
const off = ref(false);
const locked = ref(true);
const readonly = ref(true);
const picked = ref('second');
const edits = ref(0);
const deletes = ref(0);
</script>

<template>
  <section data-catalog="rows" class="grid gap-4">
    <PanelCard :title="t('dashboard.catalog.rows.summary')">
      <RowGroup>
        <SummaryRow :title="t('dashboard.catalog.rows.plain')" :detail="t('dashboard.catalog.rows.plainDetail')" />
        <SummaryRow
          pick
          chevron
          :selected="picked === 'first'"
          :title="t('dashboard.catalog.rows.pick')"
          @click="picked = 'first'"
        />
        <SummaryRow
          pick
          :selected="picked === 'second'"
          :title="t('dashboard.catalog.rows.selected')"
          :meta="t('dashboard.catalog.rows.meta')"
          @click="picked = 'second'"
        />
        <SummaryRow truncate :title="t('dashboard.catalog.rows.long')" :detail="t('dashboard.catalog.rows.longDetail')" />
      </RowGroup>
    </PanelCard>

    <PanelCard :title="t('dashboard.catalog.rows.setting')">
      <RowGroup>
        <SettingRow v-model="on" :title="t('dashboard.catalog.rows.on')" :description="t('dashboard.catalog.rows.onHelp')" />
        <SettingRow v-model="off" :title="t('dashboard.catalog.rows.off')" :hint="t('dashboard.catalog.rows.offHint')" />
        <SettingRow
          v-model="locked"
          disabled
          :title="t('dashboard.catalog.rows.disabled')"
          :description="t('dashboard.catalog.rows.disabledHelp')"
        />
        <SettingRow
          v-model="readonly"
          readonly
          :title="t('dashboard.catalog.rows.readonly')"
          :description="t('dashboard.catalog.rows.readonlyHelp')"
        />
      </RowGroup>
    </PanelCard>

    <PanelCard :title="t('dashboard.catalog.rows.editDelete')">
      <RowGroup>
        <SummaryRow :title="t('dashboard.catalog.rows.both')" :detail="t('dashboard.catalog.rows.bothDetail', { edits, deletes })">
          <template #trailing>
            <RowEditDelete
              :edit-label="t('dashboard.catalog.rows.edit')"
              :delete-label="t('dashboard.catalog.rows.delete')"
              @edit="edits += 1"
              @delete="deletes += 1"
            />
          </template>
        </SummaryRow>
        <SummaryRow :title="t('dashboard.catalog.rows.editOnly')" :detail="t('dashboard.catalog.rows.editOnlyDetail')">
          <template #trailing>
            <RowEditDelete
              :edit-label="t('dashboard.catalog.rows.edit')"
              :delete-label="t('dashboard.catalog.rows.delete')"
              :deletable="false"
            />
          </template>
        </SummaryRow>
        <SummaryRow :title="t('dashboard.catalog.rows.readOnlyRow')" :detail="t('dashboard.catalog.rows.readOnlyRowDetail')">
          <template #trailing>
            <ReadOnlyScope read-only :reason="t('dashboard.catalog.rows.readOnlyReason')">
              <RowEditDelete :edit-label="t('dashboard.catalog.rows.edit')" :delete-label="t('dashboard.catalog.rows.delete')" />
            </ReadOnlyScope>
          </template>
        </SummaryRow>
      </RowGroup>
    </PanelCard>
  </section>
</template>
