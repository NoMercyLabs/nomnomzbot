# Benchmark: default overlay widgets (what makes paid stream overlays sell)

Method: read 2026-11-10. Stoney asked for the top 10 packages of the 5 biggest overlay stores. Eight stores were
tried; five could be read. For each store 10 packages were read from the store's own product pages or its own
API, and the cover images of the top packages were looked at. Our side was measured from the 27 default widgets
in `server/src/NomNomzBot.Infrastructure/Content/Widgets/Assets/*.vue` and from a live run of 19 widgets on
dev.nomnomz.bot. The designer's brand was read from aaoa's Figma file `Y3uHgP9au3Gm9piJ68fR0s`.

Ranking limits, stated plainly: only OWN3D (install counter), Streamlabs (`recent_installs` sort) and Kudos
(best-selling sort) expose a popularity order. StreamElements and Elgato show only a featured or default order,
so their "top 10" is the store's own front row, not sales.

## Products

- OWN3D (10 of 10 read, ranked by the install counter on each page) https://www.own3d.pro/store/product/pure
- Streamlabs Library (10 of 10 read, ranked by the store API sort `recent_installs`) https://streamlabs.com/library/search?sort=recent_installs
- StreamElements Elements Gallery (10 of 10 read, Featured and Themes row order) https://widgets.streamelements.com/theme/astrology-package
- Elgato Marketplace scene collections (10 of 10 read, default listing order) https://marketplace.elgato.com/obs/scene-collections
- Kudos (10 of 10 covers and store data read, best-selling order; product pages answered 429) https://kudos.tv/collections/full-packs?sort_by=best-selling

## Coverage ledger

| Store | Packages read | Why not 10 |
| --- | --- | --- |
| OWN3D | 10 of 10 | |
| Streamlabs | 10 of 10 | 1 image seen only as a thumbnail |
| StreamElements | 10 of 10 | 4 of 10 seen by eye, the rest text only |
| Elgato | 10 of 10 | galleries of 3 packages did not render |
| Kudos | 10 of 10 | covers and Shopify data only; product pages 429 |
| Nerd or Die | 0 of 10 | Cloudflare human check; not bypassed |
| Etsy | 0 of 10 | DataDome block; not bypassed |
| Visuals by Impulse | 0 of 10 | store is gone; the domain redirects to Elgato |

## What the best sellers share

- One strong mood per pack. Every piece of a pack is clearly from the same family.
- A complete kit: scenes, camera frame, alerts, chat, labels, transition and sound.
- Two or three colors on a dark base. One accent, used little.
- Recolor without a new pack: hue filters, "+100 colors", dark and light.
- Several alert shapes, including one with no container, so the game stays visible.
- The screen edges are used. The center stays clear.
- Motion and sound are sold together with the look.
- A free pack is the front door of every store.

What looks dated: angular esports chevrons, RGB rainbow, italic gloss caps, and packs that are one template in
fifty colors.

Closest to the NomNomzBot brand: OWN3D Neon Glow and Woodlume, Streamlabs Black on Black and Pride Neon,
StreamElements Astrology and Anime, Elgato Prismatic and Planetar, Kudos Kawaii OS, Data and Cute Cats.

## The brand, as aaoa's Figma shows it

A chunky bubble "NOMZ" wordmark with a smug, smirking mouth. Print texture: halftone dots and an RGB split.
Crimson-pink and an acid yellow-lime on white and near-black `#121212`. Hand-drawn white doodle marks. White pill
buttons. It is loud and cheeky. It is not the Twitch purple `#9146ff` that 25 of 27 default widgets hard-code.

## Features

| Feature | Seen in | Source | Ours | Status | Slice |
| --- | --- | --- | --- | --- | --- |
| One visual identity across every piece | OWN3D, Streamlabs, StreamElements, Elgato, Kudos | https://www.own3d.pro/store/product/pure | Assets/*.vue: 27 widgets, 0 shared tokens, 9 radii, 10 shadows | missing | S-OVL-TOKENS |
| A written brand the pieces follow | Elgato, Kudos | https://marketplace.elgato.com/product/prismatic-4e855675-527c-4142-81f3-c762874d0738 | none; PRODUCT-ALIGNMENT.md has no brand section | missing | S-OVL-BRAND-PAGE |
| A brand typeface on every piece | OWN3D, StreamElements, Kudos | https://kudos.tv/products/cute-cats | Assets/*.vue: 25 of 27 set their own system font | missing | S-OVL-TOKENS |
| Tight palette: dark base plus one accent | OWN3D, Streamlabs, Kudos | https://marketplace-api.streamlabs.com/api/v2/themes/1614 | Assets/*.vue: 25 of 27 hard-code `#9146ff` | missing | S-OVL-MIGRATE |
| Mascot or character in the pieces | Kudos, StreamElements | https://kudos.tv/products/kawaii-os | none; the bot's face is in no widget | missing | S-OVL-TOKENS |
| Recolor the whole set with one setting | OWN3D, Elgato | https://www.own3d.pro/store/product/chroma | FirstPartyWidgetCatalogue.cs: `accentColor` is set per widget, 26 times | partial | S-OVL-RECOLOR |
| Dark and light version | Elgato | https://marketplace.elgato.com/product/planetar-69ed8d56-0593-405b-9a10-bb2edc2fb02b | none | missing | S-OVL-RECOLOR |
| Alert in several shapes | StreamElements | https://widgets.streamelements.com/theme/anime-supertheme | alerts.vue: one shape | missing | S-OVL-ALERT-SHAPES |
| Alert with no container | StreamElements | https://widgets.streamelements.com/theme/astrology-package | alerts.vue: always a card | missing | S-OVL-ALERT-SHAPES |
| Alert sound shipped with the look | OWN3D, Elgato | https://marketplace.elgato.com/product/planetar-69ed8d56-0593-405b-9a10-bb2edc2fb02b | alerts.vue: plays no sound | missing | S-OVL-SOUND |
| Enter and exit motion on every piece | OWN3D, Streamlabs, Elgato, Kudos | https://www.own3d.pro/store/product/neon-glow-animated-stream-overlay | Assets/*.vue: 5 of 27 define a keyframe animation | partial | S-OVL-MOTION |
| Respects reduced motion | none of the five stores | https://www.own3d.pro/store/product/pure | Assets/*.vue: 1 of 27 | partial | S-OVL-MOTION |
| Starting, Be Right Back and Ending screens | OWN3D, Streamlabs, StreamElements, Elgato | https://widgets.streamelements.com/theme/anime-supertheme | countdown_timer.vue and stage.vue only; no screen set | missing | S-OVL-SCENES |
| Camera frame | OWN3D, Streamlabs, StreamElements, Elgato, Kudos | https://marketplace.elgato.com/product/planetar-69ed8d56-0593-405b-9a10-bb2edc2fb02b | none | missing | S-OVL-CAM-FRAME |
| Themed chat box | Streamlabs, StreamElements, Kudos | https://widgets.streamelements.com/theme/anime-supertheme | chat_box.vue | have | |
| Labels: latest follower, top cheerer | OWN3D, Streamlabs, StreamElements, Elgato | https://www.own3d.pro/store/product/pure | labels.vue, recent_followers.vue, top_cheerers.vue | have | |
| Goal bar and event list | OWN3D, Streamlabs | https://marketplace-api.streamlabs.com/api/v2/themes/1614 | goal_bar.vue, event_ticker.vue | have | |
| Readable over a bright and a dark game | StreamElements, Elgato | https://widgets.streamelements.com/theme/astrology-package | not tested; Raid Recap shows a white scrollbar, Stage text is clipped | missing | S-OVL-BACKDROP-TEST |
| Edges used, center clear | OWN3D, Streamlabs, Elgato | https://www.own3d.pro/store/product/avoid | Assets/*.vue: 2 of 27 have a position setting | partial | S-OVL-MIGRATE |
| Works on Twitch, YouTube and Kick | OWN3D, Streamlabs, StreamElements | https://www.own3d.pro/store/product/pure | chat_box.vue shows platform; alerts are Twitch-shaped | partial | S-OVL-MIGRATE |
| Text in the viewer's language | OWN3D | https://www.own3d.pro/store/product/pure | Assets/*.vue: 0 of 27 use translations | missing | S-OVL-I18N |
| A voice: the pack talks in character | Kudos | https://kudos.tv/products/kawaii-os | none; default texts are plain system lines | missing | S-OVL-VOICE |
| One-click install of the whole set | OWN3D, Streamlabs | https://streamlabs.com/library/search?sort=recent_installs | FirstPartyWidgetBundles.cs, FirstPartyWidgetCatalogueSeeder.cs | have | |
| Preview of the whole set before use | Streamlabs, Elgato | https://marketplace.elgato.com/obs/scene-collections | dashboard widget page: test event per widget only | partial | S-OVL-GALLERY |
| Free to use | OWN3D, StreamElements | https://widgets.streamelements.com/theme/astrology-package | every default widget is free | have | |
| Edit the piece yourself | StreamElements, Streamlabs | https://widgets.streamelements.com/theme/anime-supertheme | dashboard widget code editor (dashboard/src/api/widgetCode.ts) | have | |
| An update never overwrites a customized piece | Elgato | https://marketplace.elgato.com/product/planetar-69ed8d56-0593-405b-9a10-bb2edc2fb02b | InstalledWidgetSourceUpgradeSeeder.cs | have | |
| A check that keeps new pieces on the brand | none of the five stores | https://www.own3d.pro/store/product/pure | none; nothing stops a widget from setting its own font or color | missing | S-OVL-GUARD |
| Stinger transition between scenes | Elgato, Kudos | https://kudos.tv/products/cute-cats | none | not-doing | Reason: a stinger is a video file that OBS plays; it is not a browser widget and we ship no video assets |
| Twitch profile panels | OWN3D, Streamlabs, Kudos | https://kudos.tv/collections/full-packs?sort_by=best-selling | none | not-doing | Reason: panels are static images for the Twitch profile page, outside the bot |
| Many themed packs to pick from | OWN3D, Streamlabs, StreamElements, Elgato, Kudos | https://streamlabs.com/library/search?sort=recent_installs | none | not-doing | Reason: one NomNomzBot look done well comes first; more looks follow the recolor slice |
