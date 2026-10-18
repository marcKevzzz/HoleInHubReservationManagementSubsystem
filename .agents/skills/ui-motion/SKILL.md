---
name: ui-motion
description: Guidelines for styling with Tailwind CSS v4 and accessible GSAP motion in Hole In Hub
---

# Hole In Hub UI & Motion Skill

This skill provides design, styling, and motion standards for building accessible, luxury interfaces in Hole In Hub using Tailwind CSS v4 and GSAP, inspired by Spyker Cars and Apple Fluid Interfaces.

## 1. Design Token System

Design tokens are defined in `HoleInHub.Web/Styles/site.css` under the `@theme` directive.

### Primary Palette:
- **Obsidian Carbon**: `bg-obsidian-950`, `bg-obsidian-900`, `border-white/10`
- **Champagne Gold**: `text-champagne-400`, `bg-champagne-500`, `border-champagne-400/40`
- **Titanium Steel**: `text-titanium-100`, `text-titanium-400`
- **Racing Fairway Green**: `text-fairway-400`, `bg-fairway-900/40`, `border-fairway-500/40`

## 2. Motion Rules (Apple Design + GSAP)

1. **Respond on pointer-down**: Scale down slightly (`scale: 0.985`) instantly on press; don't wait for click.
2. **Velocity-Aware Easing**: Use `power3.out` curves for physical momentum.
3. **Always Honor Reduced Motion**: Check `window.matchMedia('(prefers-reduced-motion: reduce)')`. If reduced motion is requested, immediately set final states and skip tweens.
4. **Never hide initial content**: Initial HTML elements must not have `opacity: 0` in CSS. Animate upon JavaScript mount.
