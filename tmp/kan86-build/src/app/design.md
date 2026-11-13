# DroneOps Intelligence visual system

## Direction

Retain the existing night operations theme: deep navy surfaces, cyan interaction accents, green live/healthy states, and a restrained telemetry grid. Improve legibility through brighter text, clearer secondary text, calmer borders, and consistent spacing. Keep the existing product language, navigation, page content, and operational density.

## Color roles

- `paper` / `paper-2` / `paper-3`: application background, cards, and raised surfaces.
- `ink` / `ink-2` / `muted`: primary, supporting, and secondary text. Use muted for metadata only.
- `rule`: separators, borders, and control outlines.
- `accent`: cyan links and primary actions; `accent-ink` is the readable text on cyan.
- `success`, `warning`, `error`: semantic status colors only.
- `focus`: visible keyboard focus ring.

All shared colors and type roles are declared in `tokens.css`; page-level styles should consume these tokens instead of introducing competing palettes.

## Typography and layout

Use Segoe UI for readable interface text and Consolas for telemetry, IDs, and compact operational labels. Keep the current left-led information hierarchy and compact navigation. Prefer clear section spacing, aligned card edges, and readable form controls over decorative framing.

## Interaction

Keep routes, copy, handlers, data presentation, and controls intact. Preserve hover, active, disabled, and keyboard focus states. Honor reduced-motion preferences. Portal-specific refinements belong in `portal-theme.css` so all existing portal pages share the same surface and contrast treatment.
