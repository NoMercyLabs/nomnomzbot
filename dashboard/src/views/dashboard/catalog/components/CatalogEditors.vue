<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The editor footer (delete with its destructive confirm, or reset for a fixed item), DetailEditorPane in
// its idle, open and missing states, and ListPanel in each of its panel states.
import { ref } from 'vue';
import { useI18n } from 'vue-i18n';

import DetailEditorPane from '@/components/layout/DetailEditorPane.vue';
import EditorFooter from '@/components/layout/EditorFooter.vue';
import ListPanel from '@/components/layout/ListPanel.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import type { PanelState } from '@/composables/usePanelState';

const { t } = useI18n();

const confirmOpen = ref(false);
const deleting = ref(false);
const deleted = ref(0);
const consequences = ['chat', 'pipeline', 'history'].map((key) => t(`dashboard.catalog.confirm.${key}`));

function confirmDelete(): void {
  deleting.value = true;

  setTimeout(() => {
    deleting.value = false;
    confirmOpen.value = false;
    deleted.value += 1;
  }, 600);
}

const LIST_STATES = ['loading', 'empty', 'error'] as const satisfies readonly PanelState[];
const search = ref('');
const retries = ref(0);
</script>

<template>
  <section data-catalog="editors" class="grid gap-4">
    <PanelCard :title="t('dashboard.catalog.editors.footer')" pad>
      <div class="grid gap-4">
        <EditorFooter
          :delete-label="t('dashboard.catalog.editors.delete')"
          :discard-label="t('dashboard.catalog.editors.discard')"
          :save-label="t('dashboard.catalog.editors.save')"
          :unsaved="t('dashboard.catalog.editors.unsaved', { count: deleted })"
          :confirm-title="t('dashboard.catalog.confirm.title')"
          :confirm-description="t('dashboard.catalog.confirm.description')"
          :confirm-action="t('dashboard.catalog.confirm.action')"
          :consequences="consequences"
          v-model:confirm-open="confirmOpen"
          :deleting="deleting"
          @ask-delete="confirmOpen = true"
          @confirm-delete="confirmDelete"
        />
        <EditorFooter
          :reset-label="t('dashboard.catalog.editors.reset')"
          :discard-label="t('dashboard.catalog.editors.discard')"
          :save-label="t('dashboard.catalog.editors.save')"
          :unsaved="t('dashboard.catalog.editors.fixedItem')"
          :can-discard="false"
          :can-save="false"
        />
        <EditorFooter
          :discard-label="t('dashboard.catalog.editors.discard')"
          :save-label="t('dashboard.catalog.editors.save')"
          :unsaved="t('dashboard.catalog.editors.saving')"
          saving
        />
      </div>
    </PanelCard>

    <div class="grid grid-cols-3 gap-4 max-md:grid-cols-1">
      <div class="flex flex-col" data-catalog-pane="idle">
        <DetailEditorPane
          :open="false"
          :idle-title="t('dashboard.catalog.editors.paneTitle')"
          :idle-text="t('dashboard.catalog.editors.idle')"
          :back-label="t('dashboard.catalog.editors.back')"
        />
      </div>
      <div class="flex flex-col" data-catalog-pane="open">
        <DetailEditorPane
          open
          :idle-title="t('dashboard.catalog.editors.paneTitle')"
          :idle-text="t('dashboard.catalog.editors.idle')"
          :back-label="t('dashboard.catalog.editors.back')"
        >
          <PanelCard :title="t('dashboard.catalog.editors.open')" tier="lead" pad>
            {{ t('dashboard.catalog.editors.openText') }}
          </PanelCard>
        </DetailEditorPane>
      </div>
      <div class="flex flex-col" data-catalog-pane="missing">
        <DetailEditorPane
          open
          missing
          :idle-title="t('dashboard.catalog.editors.paneTitle')"
          :idle-text="t('dashboard.catalog.editors.idle')"
          :missing-text="t('dashboard.catalog.editors.missing')"
          :back-label="t('dashboard.catalog.editors.back')"
        />
      </div>
    </div>

    <div class="grid grid-cols-3 gap-4 max-md:grid-cols-1">
      <ListPanel
        v-for="state in LIST_STATES"
        :key="state"
        v-model="search"
        class="min-h-48"
        :title="t(`dashboard.catalog.editors.list.${state}`)"
        :placeholder="t('dashboard.catalog.search')"
        :state="state"
        :empty-text="t('dashboard.catalog.panel.emptyText')"
        :description="state === 'error' ? t('dashboard.catalog.panel.retried', { count: retries }) : undefined"
        @retry="retries += 1"
      />
    </div>
  </section>
</template>
