# Design System

- **Source of truth:** shadcn/ui (new-york), ported 1:1 to Compose — full spec in `.claude/docs/design/spec/frontend-design-system.md`.
- **Tokens:** shadcn's closed **OKLCH** contract (Tailwind v4), **neutral base**; the accent is **dynamic — derived at runtime from the signed-in user's Twitch chat color** (subtle, light + dark).
- **Components:** a closed catalogue, variants-as-data, each on the most-correct primitive (Material3-wrapped or Compose Foundation). **Icons:** the designer's pack (`IconKey`/`IconSet`), Line style, Lucide fallback.
- **Enforcement (as-built, 2026-07-20):** there is **no detekt/ktlint** and CI runs no lint pass. The real gate is local jvmTests: `DesignSystemStyleGuardTest` fails on new raw hex/`dp` in feature screens (49 pre-existing grandfathered as a shrinking per-file baseline) and on off-catalogue Material3 primitives; `StringResourceEscapingTest` bans render-breaking `\'`/`\"` in string resources. **Duplication is NOT yet gated** (a copy-paste detector / design review is still needed — this is why two poll-creation surfaces coexisted). Run via `& app\gradlew.bat -p app :composeApp:jvmTest`.
- The old Figma (`MkKBuW2Ee6T5jC8fCtZsM0`) is discarded; the HTML mockups (`nomnomzbot-design` — external archived repo, not in this tree) are a loose historical reference only.
