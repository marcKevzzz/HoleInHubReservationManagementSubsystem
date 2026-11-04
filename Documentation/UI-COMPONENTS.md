# Shared UI components and motion

Use the existing CSS components and locally bundled GSAP. Both shared layouts load the helpers; individual views should not initialize the same buttons again. Keep custom page choreography in `wwwroot/js/motion.js`.

## Buttons

| Intent | Classes | Appearance |
| --- | --- | --- |
| Primary | `button` | Filled; green on hover |
| Secondary | `button button-secondary` | Outline; transparent background |
| Tertiary | `button button-tertiary` | No border or background, including hover |

All three use the same character-roll and arrow animation. Keyboard focus remains visible. Keep component layout (width, alignment, spacing) separate from the variant. Do not invent page-specific secondary/tertiary classes.

```html
<button class="button" type="submit">Continue</button>
<button class="button button-secondary" type="button">Edit selection</button>
<a class="button button-tertiary" data-no-icon href="/">Cancel</a>
<button class="button button-tertiary" data-icon-left type="button">Back</button>
```

`data-no-icon` omits the generated arrow. `data-icon-left` places a left-facing arrow before the label. Ordinary links and semantic buttons remain functional without JavaScript. Do not use the animated button component for controls with complex nested content, such as password toggles.

`HoleInHubButtons.mount(root)` enhances newly inserted content and returns a cleanup function. The initial document is mounted automatically after DOM content is ready. Mounting an existing root or preparing a button twice does not create duplicate labels, listeners or timelines.

## Load, scroll reveal and image parallax

For simple main-page content, use explicit attributes:

```html
<section data-motion="load">Visible HTML with a gentle entrance.</section>
<section data-motion="reveal">Revealed once when it enters the viewport.</section>
<div class="photo-frame" data-parallax="7">
  <img src="/media/facebook/venue-overview.webp" alt="Simulator bays" />
</div>
```

`data-parallax` is vertical travel as a percentage in each direction (default 7, capped at 18). The frame must clip its media and the image needs enough overscan to cover that travel. Existing `.photo-frame` images provide overscan. Use a separate wrapper when combining frame reveal with image parallax. Avoid applying transforms from multiple effects to the same element.

For interactive entrances or existing page choreography:

```js
HoleInHubMotion.enter(panel); // Load, popup or step entrance; no scroll plugin needed.
HoleInHubMotion.reveal(frame, { trigger: figure, scale: .82, y: 0, duration: 1.1 });
HoleInHubMotion.parallax(image, { trigger: figure, depth: 18, scrub: .2 });
```

Call scroll helpers inside a `gsap.context()` / `gsap.matchMedia()` scope, as `animatePage()` already does. Context reversion removes their tweens and ScrollTriggers. Helpers skip animation when the user prefers reduced motion; changing that preference cancels active transient entrances and restores visible content.

For content inserted dynamically, mount only that new subtree and dispose it before removal:

```js
const disposeButtons = HoleInHubButtons.mount(panel);
const disposeMotion = HoleInHubMotion.mount(panel);
// Before removing the panel:
disposeButtons();
disposeMotion();
```

`HoleInHubMotion.mount(root)` owns its GSAP context, media-query registration and declarative effects. The main layout already mounts the page root. Custom account pages can mount a new subtree explicitly. Do not add a MutationObserver or reinitialize the entire page for a single new component.

## Verification

From `HoleInHub.Web`, run `npm run test:ui` and `npm run build:css`. Follow the repository build and .NET test checks as well. Browser checks should cover mouse hover, keyboard focus, narrow screens, and changing the operating system reduced-motion preference. JavaScript failure must leave content readable and controls usable.
