# Accessibility

A streaming interface is an accessibility problem before it is a design problem: content appears
without user action, at high frequency, in a region that may already have been read.

## What the library guarantees

**Structure.** Messages are `article` elements. The transcript is a `log` region with
`aria-live="polite"` and `aria-relevant="additions"`, so new messages are announced without the
listener being interrupted mid-sentence. A generating message carries `aria-busy`.

**Announcements are polite, and there are few of them.** Thinking indicators, agent status badges,
and copy confirmations use `role="status"` with a polite live region. Errors use `role="alert"`,
which is the one case that justifies interrupting.

**Every control is a real control.** Disclosure panels are `button` elements with `aria-expanded`
and `aria-controls`. Feedback buttons expose `aria-pressed`. Progress bars are `role="progressbar"`
with `aria-valuenow`, `aria-valuemin`, and `aria-valuemax`. The model picker is a native `select`,
which is keyboard accessible, screen-reader correct, and renders as the platform picker on mobile,
none of which a hand-rolled listbox gets for free.

**Keyboard.** Enter sends and Shift with Enter inserts a newline, which is what people expect from a
chat box and the opposite of what a plain textarea does. The slash-command and mention menus are
driven with the arrow keys, Enter, Tab, and Escape, and expose `role="listbox"` with
`aria-selected`. Choosing an item inserts it at the caret, keeps the text after it, and leaves the
caret just after the insertion. Focus rings are never removed; `:focus-visible` gets a two-pixel
outline in `--bai-color-focus`.

**Nothing fails silently.** A pasted, dropped, or picked file the composer cannot take, because it is
too large, not an accepted type, or unreadable, is reported in a dismissible notice with
`role="alert"`, so the reason is announced as well as shown.

**Icons are decorative.** Every SVG is `aria-hidden` with `focusable="false"`, and every icon-only
button carries an `aria-label`.

**Motion.** Under `prefers-reduced-motion: reduce`, animations and transitions are reduced to
nothing, smooth scrolling becomes instant, and the streaming caret stops blinking.

**Contrast.** The palettes meet WCAG AA for body text in both themes. Under `forced-colors: active`,
panels, code blocks, source cards, artifacts, and the composer take system borders so their structure
survives.

**Touch.** Hover-revealed message actions are always visible on small screens, where there is no
hover to reveal them with.

## What you must supply

**Alternative text on images.** `ImagePart.Alt` becomes the accessible name. A model will not always
provide one; ask for it in your prompt, or supply a fallback.

**Labels on generative components.** The framework cannot know what your component means. A
registered component that renders an icon or a chart needs its own accessible name.

**A page structure around the chat.** The library renders no `h1` and does not assume where it sits.

**Localisation.** `AiStrings` is English by default. Subclass it, and remember the formatters for
durations, byte counts, and token counts as well as the labels. It covers accessible names too (the
transcript region, the conversation list, citation markers), along with agent status badges, form
validation messages, conversation date groups, and the messages the composer shows when it turns a
file away.

## Testing

The component test suite asserts accessibility-critical behaviour rather than only appearance: that a
failed message renders `role="alert"`, that a pending message sets `aria-busy`, that progress is
exposed with the right values, and that every form field has a label bound to its control.

```csharp
[Fact]
public void EveryFieldGetsALabelBoundToItsControl()
{
    var component = Render<AiForm>(p => p.Add(c => c.Definition, Definition));

    var id = component.Find("label[for]").GetAttribute("for");

    Assert.False(string.IsNullOrEmpty(id));
    Assert.NotNull(component.Find("#" + id));
}
```

Automated checks are a floor, not a ceiling. Test a streaming response with a screen reader before
shipping: it is the one interaction no static check will tell you the truth about.
