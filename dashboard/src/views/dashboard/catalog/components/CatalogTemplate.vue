<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The template field with its helper panel.
import { ref } from 'vue';
import { useI18n } from 'vue-i18n';

import HelperPanel from '@/components/layout/HelperPanel.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import TemplateField from '@/components/layout/TemplateField.vue';

const { t } = useI18n();

const templateText = ref('');
const templateField = ref<InstanceType<typeof TemplateField> | null>(null);
</script>

<template>
  <section data-catalog="template" class="grid gap-4">
    <PanelCard :title="t('dashboard.catalog.template.title')" pad>
      <div class="grid gap-4 md:grid-cols-[1fr_20rem]">
        <TemplateField
          ref="templateField"
          v-model="templateText"
          context="command"
          :label="t('dashboard.catalog.template.label')"
          :placeholder="t('dashboard.catalog.template.placeholder')"
        />
        <!-- A phone shows the helper at its full height in the one page scroll (owner rule 2026-10-21). -->
        <div class="flex flex-col md:h-72">
          <HelperPanel :target="templateField" context="command" />
        </div>
      </div>
    </PanelCard>
  </section>
</template>
