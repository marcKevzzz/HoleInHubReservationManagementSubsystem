# Luxury Design System & Motion Specification

Hole In Hub combines **Tailwind CSS v4** tokens, **GSAP animations**, and principles inspired by **Spyker Cars** precision engineering and **Apple fluid motion design**.

---

## 1. Aesthetic Vision: Precision Engineering & Private Lounge Luxury

Referencing the high-impact editorial atmosphere of `spykercars.com`:
- **Obsidian & Carbon Black Surfaces**: `#050505` to `#121212` with subtle hairline borders (`border-white/10`).
- **Titanium & Brushed Steel Typography**: Crisp contrast with `#f4f4f5` and `#a1a1aa`.
- **Champagne Gold Accents**: High luxury `#d4af37` and `#c5a059` highlights for primary actions and VIP indicators.
- **Deep Fairway Green**: `#052e16` and `#15803d` for organic golf heritage.
- **Editorial Micro-Typo**: Monospaced uppercase tracked metadata (`font-mono tracking-[0.15em] uppercase`).

---

## 2. Color Tokens (`Styles/site.css`)

| Palette | Prefix | Hex Codes | Usage |
| :--- | :--- | :--- | :--- |
| **Obsidian Carbon** | `obsidian-*` | `#050505`, `#0a0a0a`, `#18181b` | Main backgrounds, glass cards, deep contrast panels |
| **Champagne Gold** | `champagne-*` | `#d4af37`, `#c5a059`, `#7a5e27` | Primary brand CTAs, VIP reserve badges, focus accents |
| **Titanium Steel** | `titanium-*` | `#f4f4f5`, `#d4d4d8`, `#71717a` | High-legibility text, secondary labels, borders |
| **Fairway Racing Green** | `fairway-*` | `#22c55e`, `#15803d`, `#052e16` | Active telemetry lights, confirmed statuses |

---

## 3. Motion Architecture (`motion.js`)

Incorporating Apple Design WWDC principles:
1. **Direct Tactile Feedback**: Buttons and cards respond on pointer-down with scale damping (`scale(0.985)`), not waiting for release.
2. **Velocity-Aware Transitions**: Easing follows `power3.out` curves for natural inertia.
3. **Interruptible Transitions**: Animations can be reversed without jarring stops.
4. **Accessible Reduced-Motion**: If the client OS has `prefers-reduced-motion` enabled, animations instantly complete or are bypassed.
5. **No FOIC (Flash of Invisible Content)**: Markup remains fully readable and rendered if JavaScript is disabled.

---

## 4. Accessibility Standards (WCAG 2.2 AA)

- **Visible Focus Rings**: `focus-visible:ring-1 focus-visible:ring-champagne-400 focus-visible:ring-offset-2`.
- **Contrast**: Minimum 4.5:1 for body copy; 3:1 for large headings.
- **Skip Link**: Top-level `Skip to main content` keyboard shortcut.
