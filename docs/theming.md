# Theming

Every visual decision reads from a custom property. Overriding one on `:root`, on a container, or on
a single component instance restyles the framework without touching its source.

```css
:root {
  --bai-accent: #7c3aed;
  --bai-radius: 14px;
  --bai-content-width: 52rem;
  --bai-font-sans: "Inter", system-ui, sans-serif;
}
```

## The tokens

| Group | Examples |
| --- | --- |
| Surfaces | `--bai-color-bg`, `-bg-subtle`, `-bg-muted`, `-bg-inset`, `-bg-overlay` |
| Text | `--bai-color-fg`, `-fg-muted`, `-fg-subtle`, `-fg-inverted` |
| Lines | `--bai-color-border`, `-border-strong`, `--bai-color-focus` |
| Brand | `--bai-accent`, `--bai-accent-dark`, `--bai-accent-fg`, `--bai-accent-fg-dark` (inputs; see below) |
| Intent | `--bai-color-accent`, `-success`, `-warning`, `-danger`, `-info`, each with a `-subtle` pair |
| Messages | `--bai-user-bg`, `--bai-user-fg`, `--bai-assistant-bg`, `--bai-assistant-fg` |
| Type | `--bai-font-sans`, `-mono`, `--bai-font-size`, `-sm`, `-xs`, `-lg`, `--bai-line-height` |
| Space | `--bai-space-1` through `--bai-space-7` |
| Shape | `--bai-radius-sm`, `--bai-radius`, `-lg`, `-full` |
| Elevation | `--bai-shadow-sm`, `--bai-shadow`, `-lg` |
| Motion | `--bai-duration-fast`, `--bai-duration`, `-slow`, `--bai-ease` |
| Layout | `--bai-content-width`, `--bai-message-gap`, `--bai-composer-radius` |
| Syntax | `--bai-syntax-keyword`, `-type`, `-string`, `-number`, `-comment`, and nine more; optionally `-label` for lifetimes and loop labels, which otherwise use `-type` |

## Dark mode

Dark values are defined in two places so that all three ways of choosing a theme work: an explicit
choice, and the system preference where no explicit choice was made.

```css
.bai-theme-dark, [data-bai-theme="dark"] { /* explicit */ }

@media (prefers-color-scheme: dark) {
  :root:not([data-bai-theme="light"]) { /* system, unless overridden */ }
}
```

In markup:

```razor
<AiTheme Theme="AiTheme.AiThemeMode.Dark">
    <AiChat />
</AiTheme>
```

The dark palette redefines the palette tokens, including `--bai-color-accent`, and it does so on
every element that chooses a scheme. A value written straight into a palette token on `:root`
therefore does not survive the switch: the system dark rule outranks a plain `:root` rule, and a
nested `data-bai-theme` element declares its own. Brand colours go in the brand tokens instead.

## Brand accent

The accent is set through input tokens that the palette reads in both schemes:

| Token | Meaning | Default |
| --- | --- | --- |
| `--bai-accent` | The brand colour, used in both schemes. | `#2f6feb` light, `#6d9bf7` dark |
| `--bai-accent-dark` | A different shade for the dark scheme. | `--bai-accent` |
| `--bai-accent-fg` | Text drawn on the accent, such as the send button icon. | `#ffffff` light, `#10131a` dark |
| `--bai-accent-fg-dark` | Text on the accent in the dark scheme. | `--bai-accent-fg` |

`--bai-color-accent-hover`, `--bai-color-accent-subtle`, and `--bai-color-focus` are derived from
the resolved accent with `color-mix()`, so one token restyles all of them: hover is the accent shaded
toward black in light and toward white in dark, subtle is a tint of the accent over the background.

```css
:root {
  --bai-accent: #7c3aed;       /* violet in both schemes */
  --bai-accent-dark: #a78bfa;  /* a lighter violet reads better on a dark background */
}
```

The same tokens scope to a region. A brand token in an inline style takes effect on that element, so
`AiTheme` needs nothing else:

```razor
<AiTheme Style="--bai-accent:#0f766e">
    <AiChat />
</AiTheme>
```

This works whatever the scheme: the system preference, `Theme="AiTheme.AiThemeMode.Dark"` on the same or an
outer `AiTheme`, or a `data-bai-theme` attribute higher up. To brand a region from a stylesheet
instead of an inline style, add the `bai-theme` class to it, which is where the accent is worked out:

```css
.support-panel { --bai-accent: #b45309; }
```

```html
<div class="support-panel bai-theme">...</div>
```

Pick a `--bai-accent-fg` that contrasts with your accent, and set `--bai-accent-dark` or
`--bai-accent-fg-dark` when one colour cannot serve both backgrounds: the dark default text colour is
near black, which suits a light accent but not a deep one.

Overriding a palette token directly, `--bai-color-accent` included, still works on the element where
you set it and everything inside it, as long as nothing in between chooses a scheme or sets a brand
token. It does not
change the derived hover and subtle colours, which is the reason to prefer the brand tokens.

The derivation uses `color-mix()`, available in every current browser engine.

## Density

```razor
<AiTheme Density="AiDensity.Compact">
    <AiChat />
</AiTheme>
```

Compact reduces the type scale, line height, and message gap, which suits a sidebar or an embedded
panel rather than a full page.

## Scoping

Tokens cascade, so a region can differ from the page:

```razor
<AiTheme Style="--bai-accent:#0f766e; --bai-content-width:38rem">
    <AiChat />
</AiTheme>
```

## Class names

Every class is prefixed `bai-` and follows a block, element, modifier pattern:

```text
.bai-message            .bai-message--user        .bai-message__bubble
.bai-panel              .bai-panel--open          .bai-panel__header
.bai-code               .bai-code--numbered       .bai-code__header
.bai-composer           .bai-composer__row        .bai-composer__send
```

They are stable and safe to target. CSS isolation is deliberately not used: scoped attributes would
make the library harder to restyle, which is the opposite of the goal.

## Motion and contrast

Animations are short and expressed with tokens. Under `prefers-reduced-motion: reduce` every
animation and transition is reduced to nothing and the streaming caret stops blinking. Under
`forced-colors: active` the panels, code blocks, source cards, artifacts, and composer take system
borders so their structure survives.

There is also a print stylesheet: the composer, hover actions, and scroll button are hidden, and the
transcript is allowed to flow rather than scroll.
