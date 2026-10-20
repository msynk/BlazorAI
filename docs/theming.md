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

There are three ways to choose a scheme, and the stylesheet keeps them from fighting: an explicit
light region, an explicit dark region, and the system preference where nothing explicit was said.

```css
.bai-theme-light, [data-bai-theme="light"] { color-scheme: light; /* the whole light palette */ }
.bai-theme-dark,  [data-bai-theme="dark"]  { color-scheme: dark;  /* the whole dark palette */ }

@media (prefers-color-scheme: dark) {
  :root:not([data-bai-theme="light"]):not(.bai-theme-light) { /* system, on the page root only */ }
}
```

The system rule targets the page root and nothing else. An explicit region restates every palette
token on its own element, and an element's own declarations beat anything it inherits, so
`<AiTheme Theme="Light">` stays light inside a page the operating system has made dark, and
`<AiTheme Theme="Dark">` stays dark on a light page. Each explicit block also sets `color-scheme`,
so native controls, scrollbars, and form fields inside the region follow the region rather than the
page.

In markup:

```razor
<AiTheme Theme="AiTheme.AiThemeMode.Dark">
    <AiChat />
</AiTheme>
```

`AiTheme` writes `data-bai-theme` on its root for `Light` and `Dark`, and nothing for `System`. The
root also carries the `bai-theme` class, so the brand accent below is resolved on it whichever way
the scheme was chosen.

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

## Direction

The stylesheet uses logical properties throughout: `padding-inline-start` rather than
`padding-left`, `inset-inline-start` rather than `left`, `text-align: start` rather than `left`,
`border-end-end-radius` on the user bubble rather than `border-bottom-right-radius`. A right-to-left
page therefore gets a right-to-left chat with nothing more than `dir="rtl"` on an ancestor: padding,
borders, the menu position, the user bubble, and the number gutter in code blocks all flip together.
The panel chevron and the send arrow are mirrored under `[dir="rtl"]` and `:dir(rtl)` so they keep
pointing into the reading direction.

To set the direction for the chat alone, use `AiTheme`:

```razor
<AiTheme Direction="AiTheme.AiTextDirection.RightToLeft">
    <AiChat />
</AiTheme>
```

`Inherit`, the default, writes no `dir` attribute and follows the page.

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
.bai-composer__attach   .bai-composer__footer     .bai-composer__counter
.bai-composer__interim  .bai-composer__notice     .bai-composer__drop
```

The composer's counter takes `--near` when the text is close to `MaxLength` and `--over` when it
exceeds it, which is how the warning and danger colours reach it; the interim line shows dictation
that is not yet final.

They are stable and safe to target. CSS isolation is deliberately not used: scoped attributes would
make the library harder to restyle, which is the opposite of the goal.

## Motion and contrast

Animations are short and expressed with tokens. Under `prefers-reduced-motion: reduce` every
animation and transition is reduced to nothing, both by a blanket rule on everything under `.bai`
and by name for each animated class, so a component rendered outside a `.bai` root is covered too.
The streaming caret stops blinking and smooth scrolling becomes instant.

Under `prefers-contrast: more` the border tokens are redefined from the text colours on every
element that chooses a scheme, so lines darken in light and lighten in dark; focus rings widen to
three pixels; primary buttons, chips, and suggestions gain a border; the active menu option and the
active conversation take an outline; and hover-revealed message actions are always shown.

Under `forced-colors: active` the panels, code blocks, source cards, artifacts, menu, attachments,
forms, and composer take `CanvasText` borders so their structure survives; focus rings, the active
menu option, and the drop target use `Highlight` outlines, because background colours do not
survive forced colours; and progress bars, meters, and the caret are drawn in `Highlight`.

There is also a print stylesheet: the composer, its menu, hover actions, the scroll button, and the
backdrop are hidden, the footer itself is kept in case the host put printable content in it, and the
transcript is allowed to flow rather than scroll.
