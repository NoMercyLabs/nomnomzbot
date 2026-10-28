// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

import type { ChannelFeature } from '@/api/features';

// The areas of the features page, in reading order. The bot sends no area, so the page files each feature
// under the area its key belongs to; a key the page does not know yet lands in "other", never hidden.
export const featureAreaIds = ['chat', 'automation', 'other'] as const;

export type FeatureAreaId = (typeof featureAreaIds)[number];

const areaOfKey = new Map<string, FeatureAreaId>([
  ['use_7tv', 'chat'],
  ['use_bttv', 'chat'],
  ['use_ffz', 'chat'],
  ['use_link_preview', 'chat'],
  ['use_chat_html', 'chat'],
  ['use_viewer_templates', 'chat'],
  ['custom_code', 'automation'],
]);

export type FeatureArea = {
  id: FeatureAreaId;
  features: ChannelFeature[];
};

export function groupByArea(features: ChannelFeature[]): FeatureArea[] {
  return featureAreaIds
    .map((id) => ({
      id,
      features: features.filter((feature) => (areaOfKey.get(feature.featureKey) ?? 'other') === id),
    }))
    .filter((area) => area.features.length > 0);
}
