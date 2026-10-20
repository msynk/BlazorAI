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
none of which a hand-rolled listbox gets for free. The attach control is a real `button` with an
accessible name that opens the file picker; the file input behind it is kept for the browser's
sake but taken out of the tab order and hidden from the accessibility tree.

**Keyboard.** Enter sends and Shift with Enter inserts a newline, which is what people expect from a
chat box and the opposite of what a plain textarea does. Ctrl or Cmd with Enter sends whatever the
setting, so a host that turns `SubmitOnEnter` off still has a keyboard way to send. Enter during
input-method composition is never a send and never a menu choice: the component honours
`IsComposing`, and the browser-side guard checks `isComposing` and key code 229 as well. Escape
closes the menu if one is open, and stops the turn in flight if not.

**The menu is a combobox.** The textarea carries `role="combobox"`, `aria-haspopup="listbox"`,
`aria-autocomplete="list"`, and `aria-expanded`, and while the slash-command or mention menu is
open, `aria-controls` names the listbox and `aria-activedescendant` names the option the arrow keys
have reached, so focus never leaves the text. The listbox has an accessible name (`Commands` or
`Mentions`) and each option has an id and `aria-selected`. The arrow keys, Enter, Tab, and Escape
are intercepted in the browser while the menu is open, so they do not move the caret or focus.
Choosing an item inserts it at the caret, keeps the text after it, and leaves the caret just after
the insertion; moving the caret with the keyboard re-evaluates whether a menu should be open. Focus
rings are never removed; `:focus-visible` gets a two-pixel outline in `--bai-color-focus`, three
under `prefers-contrast: more`, and a `Highlight` outline under forced colours.

**Dictation is announced.** The dictation button exposes `aria-pressed`, and a visually hidden
`role="status"` region beside it says `Listening`, `Dictation stopped`, or `Dictation failed: ...`
as the state changes. Interim speech is shown under the text but not announced, because it changes
with every syllable; only the final phrase goes into the text.

**The length limit is announced before it bites.** With `MaxLength` set, the textarea takes
`maxlength` and a counter is shown under the composer. The counter is `aria-hidden`; a separate
`role="status"` region announces how many characters remain once the limit is within ten percent
or ten characters, and stays silent before that.

**Nothing fails silently.** A pasted, dropped, or picked file the composer cannot take, because it is
too large, one too many, not an accepted type, or unreadable, is reported in a dismissible notice
with `role="alert"`, so the reason is announced as well as shown, and raised to the application
through `OnAttachmentRejected` with the same reason.

## The composer

`PromptInput` (and `AiChat` on its behalf) takes these parameters beyond the text binding:

| Parameter | Default | Meaning |
| --- | --- | --- |
| `AllowAttachments`, `AllowDictation` | `true`, `false` | Offer the attach button and the dictation button. |
| `Model`, `Capabilities` | unset | The model the prompt is for, or its capabilities directly. When set, attachments are hidden unless the model declares `Vision`, `Documents`, or `Audio`; dictation is hidden unless it declares `Audio`; and `Accept` defaults to the types those capabilities imply. |
| `Accept` | unset | The file types accepted, in `accept` attribute syntax. Checked for pasted and dropped files too. |
| `MaxAttachmentSize` | 10 MB | The largest file. Checked in the browser before anything is read. |
| `MaxAttachments` | 20 | The most files staged at once. |
| `MaxInlineSize` | 256 KB | The largest file whose bytes are held in memory. Larger files are read on demand through `Attachment.ContentFactory`; image thumbnails are browser object URLs either way, so a circuit never holds a base64 copy of a photograph. |
| `MaxLength` | unset | The most characters; enforced by the textarea and shown as a counter. |
| `DraftKey` | unset | Persist the draft in `sessionStorage` under this key. |
| `DictationLanguage` | browser | The speech recognition language. |
| `Commands`, `Mentions` | unset | The slash commands and the mention items. |
| `Placeholder`, `Disabled`, `Generating` | | The usual. `Generating` falls back to the session. |
| `Leading`, `Trailing`, `Hint` | | Render fragments before the text, before the send button, and under the composer. |

Events: `ValueChanged`, `OnSubmit` (text plus staged attachments), `OnStop`, and
`OnAttachmentRejected` (file name, type, size, and a reason of `TooLarge`, `TooMany`,
`TypeNotAllowed`, or `ReadFailed`).

**Drafts.** With a `DraftKey`, the text is saved to the browser's session storage on every change,
restored when the composer first renders with an empty value, and cleared on submit. Session
storage rather than local storage, so a draft never follows the user to another tab or another day.
Use a key that identifies the conversation, so drafts do not bleed between them.

**Attachments and memory.** A pasted or dropped file is described to the server first, with its
size, and read only if it passes the size, count, and type checks; the bytes travel as a stream in
circuit-sized chunks, never in one interop message. A file within `MaxInlineSize` is buffered and
carries `Data` and a data URL. A larger one carries a `ContentFactory` that opens the browser file
when something reads it, and the browser-side stream is released when the attachment is removed or
the composer is disposed before sending. Attachments that were sent belong to their message and are
left alone.

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
transcript region, the conversation list, citation markers, the command and mention menus), along
with agent status badges, form validation messages, conversation date groups, the dictation status
announcements, the character counter, the deprecated-model label, and the messages the composer
shows when it turns a file away.

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
