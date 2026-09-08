# Theming

Every visual decision reads from a custom property. Overriding one on `:root`, on a container, or on
a single component instance restyles the framework without touching its source.

```css
:root {
  --bai-color-accent: #7c3aed;
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
| Intent | `--bai-color-accent`, `-success`, `-warning`, `-danger`, `-info`, each with a `-subtle` pair |
| Messages | `--bai-user-bg`, `--bai-user-fg`, `--bai-assistant-bg`, `--bai-assistant-fg` |
| Type | `--bai-font-sans`, `-mono`, `--bai-font-size`, `-sm`, `-xs`, `-lg`, `--bai-line-height` |
| Space | `--bai-space-1` through `--bai-space-7` |
| Shape | `--bai-radius-sm`, `--bai-radius`, `-lg`, `-full` |
| Elevation | `--bai-shadow-sm`, `--bai-shadow`, `-lg` |
| Motion | `--bai-duration-fast`, `--bai-duration`, `-slow`, `--bai-ease` |
| Layout | `--bai-content-width`, `--bai-message-gap`, `--bai-composer-radius` |
| Syntax | `--bai-syntax-keyword`, `-type`, `-string`, `-number`, `-comment`, and nine more |

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

Because the dark palette redefines only the tokens, a custom accent set on `:root` survives the
switch. Define your own colours as tokens rather than as rules and they will too.

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
<AiTheme Style="--bai-color-accent:#0f766e; --bai-content-width:38rem">
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
