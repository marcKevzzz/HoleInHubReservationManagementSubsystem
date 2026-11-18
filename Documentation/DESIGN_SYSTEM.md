# Current design system and motion

Reviewed against Styles/site.css and local assets on 2026-11-04. The old obsidian/champagne design is historical; current pages use white surfaces, charcoal text and muted green. [UI-COMPONENTS.md](UI-COMPONENTS.md) documents shared controls and motion conventions.

## Tokens and fonts

Tailwind CSS 4.3 reads tokens from `HoleInHub.Web/Styles/site.css` under `@theme`, with reservation, availability and account-hub styles imported locally.

| Token | Current value | Purpose |
| --- | --- | --- |
| ink | `#10110f` | Main text |
| paper | `#fff` | Main surface |
| green | `#354f3b` | Brand/actions |
| muted | `#60645d` | Secondary text |
| line | `#d5d7d1` | Dividers |
| control-line | `#83877f` | Control boundary |
| error | `#9b3027` | Error feedback |
| auth-shade | `rgb(16 17 15 / 78%)` | Authentication overlay |

Use semantic tokens rather than inventing ad-hoc colors. Display typography uses locally hosted Oswald; body typography uses locally hosted Inter, with system fallbacks. CSS includes corresponding custom properties; keep them synchronized when changing theme values.

## Assets and motion

GSAP 3.13 is installed through npm. `npm run copy:vendor` copies GSAP, ScrollTrigger and ScrollSmoother into local vendor assets; no runtime CDN is needed. `npm run build` copies vendor files and builds CSS; `npm run watch:css` watches stylesheet changes.

Shared UI/button motion, page transitions, account navigation and booking scripts are separate files under wwwroot/js. Consult their source and UI-COMPONENTS rather than assuming all behavior lives in motion.js. Animation is progressive enhancement: initial content remains visible and forms remain usable if JavaScript fails. Respect prefers-reduced-motion, final states, interruption and cleanup.

## Accessibility requirements and verification

Maintain visible keyboard focus, the contributor-required two-pixel focus-ring treatment, semantic labels/errors and WCAG 2.2 AA contrast: 4.5:1 normal text and 3:1 large text/icons. These are requirements, not a blanket certification of current pages. Verify focus order, disabled/loading controls, reduced motion and responsive overflow when changing UI.

Run `npm --prefix HoleInHub.Web run test:ui` and `npm --prefix HoleInHub.Web run build:css`; perform browser checks for visual and accessibility behavior that source tests cannot prove.

## Staff component consistency

`Styles/staff-components.css` owns shared staff panel dimensions, typography, controls, hover states, and badge geometry. Use `staff-side-panel`, a header with `staff-panel-heading`, and a scrollable `staff-panel-body` for every workspace side panel. Activity, schedule, and account forms share `HoleInHubPanels.loading(body, label)` from `staff-panel.js`; remove `aria-busy` when the request settles.

Staff text buttons and back buttons use the shared staggered rolling-text animation, with right arrows for actions and left arrows for back navigation. Fetched content must mount the same button component; progress labels use its setLabel API. Text links use a sliding ::after underline. Text fields use the sign-in underline focus animation without an outline. Side-panel loading regions show skeletons with a visually hidden loading announcement, never a spinner. Reservation statuses use `staff-status` and their existing semantic state colors; people metadata uses `people-pill` with the same sizing and rounded shape. Team-member activity is an embedded feed, with no filter-clearing action in its empty state.
