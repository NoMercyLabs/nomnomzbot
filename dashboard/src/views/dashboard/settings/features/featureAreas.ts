// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
