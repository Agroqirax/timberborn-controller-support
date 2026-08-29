# Timberborn modding notes (v1.1.2.0)

Reference notes gathered while building this mod, so the basics don't have to be
re-discovered. Decompiled game source (AssetRipper) lives at
`~/Documents/timberborn-exports/1.1.2.0-cf8e8d1-xsw/Scripts/`, one folder per assembly.

**Update `CHANGELOG.md` whenever a fix/feature/change lands** - add an entry under
`## [Unreleased]`'s `### Added`/`### Changed`/`### Fixed` (create the section if it's not there yet),
matching the terse one-line-per-entry style already used for every prior release.

## `.meta` files

Never hand-write a `.meta` file. Unity regenerates them (new guid included) the next
time the project is opened in the Editor - a mod source file with no `.meta` yet is
fine to leave as-is until then. Deleting a stray/orphaned `.meta` is usually safe for
the same reason - it comes back on the next Editor open.

## Build / deploy

- `tbuild` (`~/.local/bin/tbuild`) is the build script; it's also the default VSCode/Zed build task.
  It finds `Scripts/*.asmdef`, resolves the sibling auto-generated `.csproj` at the Unity project
  root, `dotnet build`s it in Release straight into `~/Timberborn/Mods/<ModFolderName>/Scripts`,
  then copies `manifest.json` plus `Root/` and `Data/` if present.
- Unity generates one `.csproj` per `.asmdef`, and it lists source files **explicitly**. Adding a
  new `.cs` file therefore breaks the build until Unity regenerates the csproj (open the project in
  the Editor) — or you add the `<Compile Include="..."/>` line by hand as a stopgap.
- Player log (Proton/Steam):
  `~/.var/app/com.valvesoftware.Steam/.steam/steam/steamapps/compatdata/1062090/pfx/drive_c/users/steamuser/AppData/LocalLow/Mechanistry/Timberborn/Player.log`

## DI and lifecycle (Bindito — no Harmony needed for most things)

- The game is wired with **Bindito** (`Bindito.Core`). Mods add a `Configurator` subclass tagged
  `[Context("MainMenu")]` / `[Context("Game")]` / `[Context("MapEditor")]` and bind types in
  `Configure()`. This is Bindito's own scoping attribute — nothing to do with BepInEx.
- Lifecycle interfaces live in `Timberborn.SingletonSystem`: `ILoadableSingleton`,
  `IUnloadableSingleton`, `IPostLoadableSingleton`, `IUpdatableSingleton`, `ILateUpdatableSingleton`.
  `ILoadableSingleton` is marked `[Singleton]`, which is what makes Bindito eagerly construct and
  `Load()` anything bound that implements it — `Bind<X>().AsSingleton();` is all that's required.
- There's also an `EventBus` with `[OnEvent]` methods for pub/sub.
- Harmony is only needed to change behaviour of sealed/internal methods with no extension point.

## Input

- Two layers: the **new Unity Input System package** (`UnityEngine.InputSystem`) for raw devices,
  and Timberborn's own `InputService` (`Timberborn.InputSystem`) facade on top.
- `InputService` is an `IUpdatableSingleton` exposing semantic properties (`UIConfirm`, `UICancel`,
  `MainMouseButtonDown`, `MouseOverUI`, …) driven by the rebindable `KeyBindingSystem`.
- **Input processor chain**: implement `IInputProcessor` (single `bool ProcessInput()`), register
  with `_inputService.AddInputProcessor(this)` in `Load()` and remove in `Unload()`. Each frame
  `InputService.UpdateSingleton()` → `CallInputProcessors()` walks processors **last-registered
  first**, stopping at the first that returns `true`. Return `false` to let others run.
  Reference implementation: `Timberborn.DuplicationSystemUI/DuplicationInputProcessor.cs`.
- `PanelStack` is itself an `IInputProcessor`, registering on `Show()` / unregistering on `Hide()`.
  Note its `ProcessInput()` returns `TopPanel.IsOverlay` — so while an *overlay* panel is on top it
  swallows input for every processor registered before it. Since nearly every dialog is an overlay,
  a mod processor registered once at load is dead for most of the UI. The fix is to re-register
  (`Remove` then `Add`) on every `PanelShownEvent`/`PanelHiddenEvent`, which puts the mod back at the
  end of the list and therefore first in the walk. This has to be driven by the event: once buried,
  the processor never runs again to notice. Mutating the list from inside a processor is safe —
  `CallInputProcessors` iterates a copy.
- **The game has zero gamepad support**: no `Gamepad`/`Joystick` references anywhere, and no
  `InputSystemUIInputModule`. Gamepads must be polled directly via `Gamepad.current`.
- Keybindings are blueprint-driven (`KeyBindingSpec`, `KeyBindingRegistry`) and read real
  `InputControl`s, so they can't be set from code. To trigger one, synthesise the device input, e.g.
  `InputSystem.QueueStateEvent(Keyboard.current, keyboardStateWithEscape)`. **Press and release must
  land in different frames** — queueing both in one frame lets the release overwrite the press
  before the game polls it.

## UI — Unity UI Toolkit (UIElements), not uGUI

- Everything is UI Toolkit: `UIDocument` + UXML (`VisualTreeAsset`) + USS. There is no uGUI
  (`UnityEngine.UI`), no `Selectable`, no `EventSystem`-driven navigation. The one `EventSystem`
  reference in the whole game is `InputService` calling `IsPointerOverGameObject()` for `MouseOverUI`.
- Custom controls in `Timberborn.CoreUI`: `NineSliceButton : Button`, `LocalizableButton : Button`,
  `NineSliceLabel`, `LocalizableToggle`, `RadioToggle`, … — so matching `Button` by type catches the
  Timberborn subclasses too.
- Click handling is **always** `RegisterCallback<ClickEvent>(...)`; the game never uses
  `button.clicked +=`. This matters a lot (see pitfalls).
- `VisualElementInitializer` / `IVisualElementInitializer` is an extension point that post-processes
  every element (e.g. `ButtonClickabilityInitializer` widens click activators).
- **Panel hierarchy**: `TitleScreen.Initialize()` calls
  `PanelStack.Initialize("MainMenu/TitleScreen", "TitleScreenContent")`, which creates the single
  `UIDocument` on a GameObject named exactly **`PanelStack`** (via
  `RootVisualElementProvider.CreateEmpty("PanelStack", 1)`). Every pushed panel is added as a child
  of the `TitleScreenContent` container inside it. `GameObject.Find("PanelStack")` will reach the
  live UI tree, but don't — see below, `PanelStack` itself is injectable and knows this exactly.

## Tracking which panel is in front (the useful hooks)

Don't re-derive the front panel by scanning the container for its last visible child. `PanelStack`
already tracks it precisely, and everything needed to mirror that is reachable:

- `PanelStack` is bound `AsSingleton()` by `CoreUIConfigurator`, which is tagged for **all three**
  contexts (`MainMenu`, `Game`, `MapEditor`) — so a mod can just take it as a constructor dependency.
  Same for `UISoundController` (`UISoundConfigurator`), which gives `PlayClickSound()` /
  `PlayCancelSound()` so mod-driven activation sounds like the real thing.
- It posts **`PanelShownEvent`** and **`PanelHiddenEvent`** on the `EventBus` on every change, so a
  mod can react instead of polling. Both are public. Note the ordering inside `Show()`: the element
  is added and `AddInputProcessor` runs *before* the event is posted, which is what makes
  re-registering from the handler land in the right place.
- The stack field (`_stack`) is private and `StackedPanel` is an internal struct, but its
  `PanelController` and `VisualElement` properties are public, so one reflected field read plus two
  property reads gets both. `Stack<T>` enumerates top-first, so the first entry is the front panel.
- **`IPanelController` is public**, with `GetPanel()`, `OnUIConfirmed()` and `OnUICancelled()`. This
  is the big one: a mod can drive a panel's own back/confirm behaviour by calling these directly,
  rather than synthesising an Escape key press onto the real keyboard device. It also means the
  front panel has a stable *type* to key per-panel navigation rules on.
- `StackedPanel.VisualElement` is the element that was actually pushed — for overlay and dialog
  panels that's the overlay wrapper, not the panel itself. That's the right navigation scope: it is
  the whole of what the player can currently interact with.
- **Red herring**: the `Timberborn.AccessibleNavigation` assembly is beaver *pathfinding*
  (`IAccessibleNeeder`), nothing to do with UI accessibility or focus.
- Lists (saves, maps, settlements) are `ListView`s whose rows are **plain `VisualElement`s** built by
  a factory with `RegisterCallback<ClickEvent>` — they are *not* `Button`s. See
  `Timberborn.GameSaveRepositorySystemUI/SaveList.cs`.

## The Game and MapEditor scenes lay out UI differently

The MainMenu puts everything on the panel stack, so "the front panel" is the whole story there. The
Game scene does not:

- `UILayout` (`Timberborn.UILayoutSystem`, `ILoadableSingleton`) calls
  `PanelStack.Initialize("Common/GameUI", "Panels")` and then hangs the HUD off **sibling containers
  of the `Panels` container** — `Top-left`, `Top-right`, `Top-bar`, `Bottom-left`, `Bottom-right`,
  `Bottom-bar`, `Absolute-items` — via `AddTopLeft` / `AddBottomBar` / `AddAbsoluteItem` / etc.
- So the bottom bar, entity panel (`Absolute-items`), district panel, notifications and alerts are
  **not on the panel stack**. Only dialogs and boxes are. An empty stack means "no dialog is up",
  not "there is nothing to interact with".
- The usable fallback is `PanelStack`'s private `_root` — the `UIDocument` root returned by
  `Initialize` — which is the only element covering both the HUD containers and the panel container.
- The HUD also changes without any panel event (an entity panel appears when something is selected),
  so anything cached per panel-event will go stale there. Re-deriving on demand is the safer default.

## Camera

`CameraService` is public and bound in `Game`/`MapEditor` by `CameraSystemConfigurator`.
`MoveCameraBy(Vector3)` already rotates the delta by the camera's `HorizontalAngle`, so passing a
stick vector straight through gives camera-relative panning with no extra maths. `ModifyZoomLevel`,
`ModifyHorizontalAngle` and `ModifyVerticalAngle` are there too.

`KeyboardCameraController` is the reference implementation, and worth matching: its speed is
`(InputSettings.KeyboardCameraMovementSpeed * 50 + 1) * CameraService.ZoomSpeedScale * dt`, with `dt`
capped at 0.2s so a frame hitch cannot fling the camera. `InputSettings` is public in
`Timberborn.InputSystem`; note Unity ships its own `InputSettings`, so the using has to be aliased.
`CappedTime` is internal — inline the `Min(unscaledDeltaTime, 0.2f)` instead.

**A continuous input must never return `true` from `ProcessInput`.** `CallInputProcessors` stops at
the first processor returning true, so a processor that claims a held stick freezes everything
behind it for as long as it is held — camera panning and the game's own WASD camera included. This
bites hardest for a processor that re-registers itself to the front of the queue. Momentary presses
are fine to claim; held axes are not.

## Reading the game's own UXML and USS

Every view and stylesheet the game ships is in plain text inside:

```
<steam>/steamapps/common/Timberborn/Timberborn_Data/StreamingAssets/Modding/UI.zip
```

(Under Flatpak Steam that is `~/.var/app/com.valvesoftware.Steam/.local/share/Steam/...`.)

408 files, `Views/**/*.uxml` and `Views/**/*.uss`. The AssetRipper export does **not** contain
these, so read them from here. This is the fastest way to answer "what is this element called",
"is it a sibling or a child", and "does this thing have a `:hover` rule at all" — all of which
are otherwise guesswork against decompiled factory code.

The sibling `Blueprints.zip` in the same folder has the actual data behind every `ComponentSpec` —
`Configurations/<SpecRecordName minus "Spec">.blueprint.json`, e.g. `SelectionColorsSpec` reads
from `Configurations/SelectionColors.blueprint.json`. The decompiled C# only gives field names
(`Color EntitySelection { get; init; }`), never the real values — Timberborn names spec fields by
*role*, not by what they look like, so a plausible-sounding name like `SelectionToolHighlight` or
`ToolActionTile` can turn out to be a colour you didn't expect (both are actually dark red, not
amber). Check this zip before writing code that assumes a spec value.

Worth knowing about two elements in particular:

- `ExtendableTopBarCounter.uxml` — `CounterWrapper` (the box) and `ExtensionToggler` (the little
  arrow) are **siblings**, not nested, and `TopBarCounterFactory` wires the same toggle onto both.
  Only the arrow has a `:hover` rule; the box has none.
- `BottomBarPanel.uxml` — `SubSection` and `MainSection` are siblings under `BottomBar`. Category
  buttons go into `MainSection`; each category's tool row is *reparented* into `SubSection` by
  `BottomBarPanel.AddElement`, and all but the open one are hidden.

### Overriding them needs Unity; adding styles from code does not

`VisualElementLoader` goes through `IAssetLoader.Load<VisualTreeAsset>("UI/Views/...")`, which
resolves out of asset bundles. Overriding a shipped `.uxml`/`.uss` therefore means shipping an
asset bundle at the same path (see the sibling `TimberbornCycleRewards` mod), and building an
asset bundle means opening Unity — which the rest of this mod does not need.

There is no runtime API for authoring a `StyleSheet` from code either (`StyleSheetBuilder` is
editor-only). So when an element needs a look the theme does not give it, the code-only answer is
inline styles. `SelectionHighlighter` draws its selection ring as an absolutely positioned child
overlay rather than as a border on the control: UI Toolkit lays out border-box, so widening the
control's own border eats into its padding and shoves the contents around.

## Overriding a keybinding from a mod - JSON merge, not asset-bundle overriding

Unlike UXML/USS above (which need an asset bundle to override), a mod can extend an existing
`Blueprints/KeyBindings/<Group>/KeyBinding.<Id>.blueprint.json` by shipping a file at the exact
same relative path. `Timberborn.SerializationSystem.JsonMerger` folds every provider's JSON for
that path into one document (`JObject.Merge`, in `Order` sequence - base game first, mods after)
*before* any spec (`KeyBindingSpec`, `PrimaryInputBindingSpec`, `SecondaryInputBindingSpec`) is
deserialized. So a mod file containing only `{"SecondaryInputBindingSpec": {...}}` merges cleanly
onto the base game's spec as a new top-level property - no `#append`/`#merge` JSON-keyword suffix
needed for a plain object key, those are for array properties.

**The catch: there is only Primary and Secondary, no third slot.** If the base blueprint already
has a `SecondaryInputBindingSpec` (e.g. `DeleteObject` already uses it for Backspace, `Confirm`
for numpad Enter), a mod's own `SecondaryInputBindingSpec` for that same action **replaces** it
outright rather than adding a third binding - decide up front whether that trade-off is
acceptable, since it silently drops the existing default for every player.

**Path syntax gotcha:** every existing keyboard blueprint uses the bare form (`/Keyboard/escape`,
`/Keyboard/r`). That only resolves because Unity's singleton Keyboard/Mouse devices are literally
named `"Keyboard"`/`"Mouse"`. A real gamepad is named after its product string, not `"Gamepad"`,
so the same bare-name form (`/Gamepad/buttonEast`) would silently never match anything - no error,
`InputBinding.IsDefined` stays true, the control just never resolves and the binding is dead. Use
Unity's layout-matching syntax instead: `<Gamepad>/buttonEast` (angle brackets mean "any device
implementing the Gamepad layout"), which is how `Timberborn.InputSystem.InputBinding` resolves the
path via the stock `InputSystem.FindControl`/`InputControl.IsPressed` APIs either way - fully
generic, no keyboard/gamepad special-casing anywhere in `Timberborn.KeyBindingSystem`.

**Registering a binding is often all that's needed - check for a native consumer before writing
mod code.** Several base-game systems already watch specific keybinding IDs and are otherwise
inert for a device that was never bound to them:
- `PanelStack.ProcessInput()` calls the front panel's `OnUICancelled()`/`OnUIConfirmed()` on
  `Cancel`/`Confirm` respectively - the *only* place either is consumed for panel dismiss/default
  action. `PanelStack` isn't even registered as an `IInputProcessor` while no panel is stacked, so
  there's no window where it fires outside a dialog.
- `ToolService` and `ToolGroupService` each separately watch `InputService.Cancel` to drop the
  active tool / close the open bottom-bar row - two more processors, not one.
- `DropdownListDrawer` closes itself watching the same `Cancel` signal.
- `BlockObjectPlacementPanel` wires `RotateClockwise`/`RotateCounterclockwise`/`Flip` to
  `PreviewPlacement` via small per-button `BindableButton` (`Timberborn.InputSystemUI`) processors,
  bound/unbound on `ToolEnteredEvent`/`ToolExitedEvent` for `BlockObjectTool` - and only binds Flip
  at all when the object is actually flippable, which a mod's own unconditional call would not
  know to skip.

If a mod's own code duplicates one of these (rather than deleting it once the blueprint takes
over), don't assume the duplication is harmless just because it doesn't error: a repeated
`RotateClockwise()` on the same press rotates twice as far, a repeated `Flip()` flips right back
to where it started. `Cancel` happens to be safe to double up on because closing an already-closed
dialog is a no-op either way - that's a property of `Cancel` specifically, not of double-handling
in general.

**Where to place the file for `tbuild`:** the game's `ModSystemFileProvider` scans a mod's
installed root recursively - no reserved `Blueprints/` folder name or manifest declaration
required, the asset key is just the file's path relative to that root, mirroring `Blueprints.zip`'s
own internal layout 1:1 (e.g. `KeyBindings/UI/KeyBinding.Cancel.blueprint.json`, no leading
`Blueprints/`). `tbuild` copies `./Root/**` flattened straight into the installed mod's root
(`cp -r ./Root/. "$MODDIR/"`), so the source file belongs at
`Root/KeyBindings/UI/KeyBinding.Cancel.blueprint.json` in the mod's own repo.

## Controls (what a "single control" actually is)

- Timberborn's controls are **composites**, and their inner parts are independently clickable. A
  `Slider` is a track plus a drag container plus a dragger; `PreciseSlider : VisualElement` wraps a
  `Slider` with Decrease/Increase `Button`s; `Dropdown : VisualElement` holds `Selection`,
  `ArrowDown`, `ArrowLeft`, `ArrowRight` buttons. Any navigation scheme that walks the tree must
  treat these as **leaves**, or one control becomes a scatter of candidates at slightly different
  positions — which shows up as a control that highlights when approached from one direction and
  not another.
- The `Dropdown` itself carries no click handler; it delegates to an inner button. `Selection` when
  the field is clickable (class `dropdown__selectable`), `ArrowDown` when the UXML sets
  `buttons-only-selection`. In buttons-only mode `ArrowLeft`/`ArrowRight` step the value; otherwise
  both are `display: none`.
- `LocalizableSlider : Slider`, `LocalizableSliderInt : SliderInt`, `LocalizableToggle : Toggle` —
  so type checks catch them. `PreciseSlider` and `Dropdown` are plain `VisualElement`s and need
  naming explicitly. `RadioToggle` is not a `VisualElement` at all — it's a controller wrapping real
  toggles.
- Setting `PreciseSlider`'s inner `Q<Slider>("Slider").value` propagates correctly: it registers
  `RegisterValueChangedCallback` on that slider in its constructor.

## Navigation feel, and two things that are deliberately absent

- **No wrapping.** Reaching the end of a row or column stops there. It used to wrap to the far end,
  which reads fine in a long settings list but is actively confusing in the two-row toolbar: pushing
  up from the top row dropped you back onto the bottom one, and repeated up/down walked the
  selection sideways across the bar.
- **Ties break on cross-axis centre distance, not tree order.** The toolbar's two rows are often
  offset by half a button, so a push upwards overlaps two buttons equally and both sit exactly the
  same distance away. Taking the first one found meant always taking the left one - which is what
  produced the sideways walk above.
- **Eight directions, unevenly sliced.** A diagonal only registers when the weaker stick axis is at
  least 55% of the stronger one, so diagonals get a narrow band and the cardinals stay generous. A
  diagonal move requires a candidate genuinely in that corner and does nothing otherwise, so a
  sloppy push meaning "up" must not be read as one.

## Adding a child to an element destroys its text measurement

`VisualElement.hierarchy.Add` contains this:

```csharp
if (m_Owner.layoutNode.UsesMeasure) { m_Owner.RemoveMeasureFunction(); }
```

Yoga only allows a measure function on a leaf node, so **any element that measures its own content
stops doing so the moment it gains a child**. For a text-only `Button` or `Label` that means its
auto width collapses to zero and the text wraps one character per line — a vertical stack of
letters. It is restored when the last child is removed, so the damage is invisible in a diff and
only shows while something is parented.

This is why `SelectionHighlighter` parents its selection ring to the selected element's **parent**
and positions it from `element.layout`, rather than filling the element itself with an
`inset: 0` overlay. A parent always has at least one child already, so it cannot regress.

Note `layout` is measured from the parent's border box while absolute `left`/`top` are measured
from its padding box, so the ring subtracts the parent's border widths.

## ListView rows are not always clickable

Two lists sit side by side in the load-game menu and only one of them worked. `SaveList` calls
`visualElement.RegisterCallback<ClickEvent>(...)` on each row it builds, so its rows are ordinary
click candidates. `SettlementList` does not — it leaves selection entirely to the `ListView`, whose
own pointer handling never registers a `ClickEvent` — so it offered nothing to aim at.

The rule is therefore *fall-through*, not *type*: a `BaseVerticalCollectionView` becomes a source of
row candidates only when nothing inside it already qualified. Either way every realised row
(`GetRootElementForIndex`, `NavigationCandidates.CollectRows`) ends up as its own candidate, click
handler or not — a list is never a single candidate spanning every row.

That used to be different: a list with no per-row click handler was one candidate, and up/down drove
its `selectedIndex` directly, refusing the push at either end so the player could still leave.
`ModUploaderBox`'s local-mods list broke that: its Upload button sits *below* the list, so the
refusal-at-the-end rule meant the only mod you could ever upload was whichever one you happened to be
on when you finally hit the last row and the push escaped downward. Per-row candidates fix it by
letting ordinary spatial navigation leave from wherever the player currently is, same as it already
did for `SaveList`.

Selection is driven by confirm, not by arrival. An earlier version of this called
`ControlActivator.SyncListSelection` from every selection change, which made every list preview as
the cursor moved over it - matching what `SettlementList` always did, but at the cost of the same
problem row candidates had just fixed: whatever the cursor was over when the player reached the
Upload button is what got uploaded, since moving *is* selecting. The user chose consistency over
that one preview: confirm is now the only thing that changes a `BaseVerticalCollectionView`'s
`selectedIndex`. `ControlActivator.Activate`'s default case calls `SyncListSelection` before
dispatching the synthesised click, so a row with no click handler of its own (`SettlementList`) gets
selected by hand, and one that does (`SaveList`) gets it as an explicit step rather than a side
effect of a click its own handler ignores anyway.

## Dropdown lists live outside the panel that opened them

`DropdownListDrawer` (public, `AsSingleton()` in all three contexts) builds its **own UIDocument
root** via `_rootVisualElementProvider.Create("DropdownListDrawer", "Core/DropdownItems", 2)` and
moves the item elements into a `ScrollView` inside it. So the open list is *not* a descendant of the
panel that opened it, and a walk over the front panel will never find it. `DropdownVisible` is
public; the `_items` ScrollView is private and needs one reflected field read to use as a scope.

It also registers itself as an `IInputProcessor` while open, but returns `true` only for cancel or a
click outside, so it doesn't block a mod processor. It closes on `_inputService.Cancel` — meaning a
mod that drives cancel through `IPanelController.OnUICancelled()` instead of the keybinding has to
close the dropdown itself first.

## UI Toolkit pitfalls hit while building this mod

1. **`NavigationSubmitEvent` does not click a Timberborn button.** `Button` handles it via
   `Clickable.SimulateSingleClick`, which only invokes the `clicked` delegate — but Timberborn
   registers `ClickEvent` callbacks instead. Send a `ClickEvent` to activate a control.
   **`Toggle` is the exception**: it reacts to pointer events through its `Clickable` manipulator, so
   a synthesised `ClickEvent` slides straight past it. Set `toggle.value` instead and let the
   resulting `ChangeEvent` do the work. `LocalizableToggle : Toggle` so it is covered by the same
   check; `RadioToggle` is *not* a `VisualElement` at all (it is a controller wrapping real toggles)
   and never shows up as a navigation candidate.
2. **`ClickEvent` dispatches to whatever is under the mouse pointer**, not to the element you called
   `SendEvent` on. It derives from `PointerEventBase`, whose `Dispatch()` calls
   `DispatchToCapturingElementOrElementUnderPointer(...)`. The dispatcher only falls back to the
   pointer when `elementTarget` is null, so **set `evt.target = element` before `SendEvent`**.
3. **Built-in spatial navigation is unreliable here.** Dispatching `NavigationMoveEvent` lets
   Unity's `NavigateFocusRing` pick the next element, but in Timberborn's sparser panels it
   repeatedly resolved back to the same button and got stuck. Score candidates by geometry instead:
   quantise the stick to one cardinal axis, then take the nearest candidate ahead **whose cross-axis
   band overlaps yours** — the gap measured edge-to-edge between the two elements' extents on the
   perpendicular axis, which is zero when they share a row (moving sideways) or a column (moving up
   and down).
   **Make that overlap a hard requirement, not a weighted penalty.** Weighting it looks more
   forgiving and is actively wrong on a page of stacked rows: the vertical gap between two rows is
   far smaller than the horizontal distance across one, so a sideways push scores a control in a
   neighbouring row as a fine match and jumps to it. With overlap required, a sideways push in a
   single-column list correctly does nothing. Wrap-around should carry the same requirement, so a
   list cycles without the wrap landing in a different column.
   **Hard alignment needs an escape hatch**, or a control that lines up with nothing becomes a place
   the player can enter and never leave — the title screen's Discord/Merchandise buttons sit in their
   own corner row, aligned with neither the main button column nor each other's column. Gate the
   escape on *clustering*: group candidates by "shares a row or a column with", transitively, and
   allow one unaligned hop only out of a cluster that cannot reach the rest of the panel at all. A
   settings page is one big cluster, so it never fires there. The same clustering gives a sane
   starting selection — the top-left of the **largest** cluster, so the title screen starts on the
   main menu column instead of whichever corner button sits highest on screen.
   (Stepping an index through the candidate list in *tree order* — an earlier attempt — makes
   left/right and up/down do the same thing and wraps into unrelated parts of the panel.)
4. **Focus is tracked per composite root.** `FocusController` keeps a focused element per subtree,
   so `focusController.focusedElement` never gives one coherent "current element" across panels,
   and `FocusOutEvent`-based highlight cleanup leaves stale highlights behind. Track selection in
   the mod instead.
5. **No visible `:focus` styling** in Timberborn's theme, but `:hover` is styled. Reusing the hover
   pseudo-state makes a controller selection look native. `VisualElement.pseudoStates` and the
   `PseudoStates` enum (`Hover = 2`) are `internal` → reflection required. **Setting it on one
   element is not enough**: a real mouse sets `Hover` on the element under the pointer *and all its
   ancestors*, and composite controls put their visible styling on an inner part (a Toggle's
   checkmark, a Slider's dragger, a Dropdown's field), so flagging the outer control alone leaves it
   looking unselected. Use the public `IPanel.Pick(Vector2)` to ask what a pointer resting at the
   control's centre would hit, then set the state on that element up through the control. Remember
   exactly which elements were set so they can be unset exactly. **Picking at the centre is only
   right for simple controls.** A `Dropdown`'s bounds include its label, so its centre can land well
   away from the `Selection` button that carries `dropdown__selectable`; a `Slider`'s centre sits on
   inert track. For those, name the styled part explicitly — `Selection` for a dropdown,
   `unity-drag-container` / `unity-tracker` / `unity-dragger` for a slider, `unity-checkmark` /
   `.unity-toggle__input` for a toggle — and walk up from there. Prefer naming the part even when
   picking happens to work: a pick is defeated by anything overlapping the control, and the last
   row of a scrolling page is a good place to discover that.
6. `ScrollView.ScrollTo(VisualElement)` is public — use it to keep the selection on screen inside
   scrolling panels — but it **throws** `ArgumentException` unless the element is inside that
   ScrollView's `contentContainer`. A `Scroller` (the scrollbar) holds `RepeatButton`s and a dragger
   that are descendants of the ScrollView but *not* of its content container, so a naive
   "nearest ScrollView ancestor" walk both crashes and produces ghost selections. Check
   `scrollView.contentContainer.Contains(element)` first, and skip anything under a `Scroller`.
7. Detecting "is this element clickable" generically: reflect `VisualElement.m_CallbackRegistry` →
   `m_BubbleUpCallbacks` → `m_Callbacks` (`m_Array`/`m_Count`) and compare each functor's public
   `eventTypeId` field against `EventBase<ClickEvent>.TypeId()` (which is public).
   **On its own this test is useless in Timberborn**, because `UISoundInitializer` is an
   `IVisualElementInitializer` that does `visualElement.RegisterCallback<ClickEvent>(PlayUISound)` on
   *every element in the game* — it plays whatever the element's `--click-sound` custom style names.
   So nearly everything answers yes, section headers and plain labels included. Filter that one
   callback out: read the functor's private `m_Callback` field and skip it when the delegate's
   `Target is UISoundInitializer` (both public types). Note `m_Callback` is declared on each closed
   generic functor type, so its `FieldInfo` must be cached per type — unlike `eventTypeId`, which
   lives on the shared `EventCallbackFunctorBase`. `UISoundInitializer` is the only blanket
   registrar; every other `IVisualElementInitializer` is type-specific.
8. **`IPanel.Pick` fails on anything scrolled out of view** — the point falls outside the clipped
   viewport and comes back null. So a hover-emulating highlight can't be computed for an element
   that is about to be scrolled into view; `ScrollTo` has only just been asked, and may even defer.
   Re-apply the highlight whenever the selected element's `worldBound` centre has moved instead of
   guessing at a frame count: it self-corrects for deferred scrolls and reflows, and settles.

## OnPanelChanged used to erase the selection it should have remembered

`PanelTracker.PanelChanged` fires synchronously off `PanelShownEvent`/`PanelHiddenEvent` - inside the
same call that opened or closed the panel, well before the next `ProcessInputCore`. Its handler used
to jump straight to clearing `_scope`, on the assumption that `EnterScope` would bank the outgoing
scope's selection into `SelectionMemory` the next time it ran. But `EnterScope` only remembers when
`_scope != null`, and by the time it next ran, `OnPanelChanged` had already set `_scope = null` -
every real panel open or close skipped the remember entirely. Closing a menu never got back to where
you had been, no matter how deep the memory's capacity or how careful the restore logic was, because
nothing was ever banked to restore.

This didn't affect dropdowns, which don't raise `PanelShownEvent`/`PanelHiddenEvent` at all - their
scope only changes inside `ProcessInputCore`, where `EnterScope` still sees the real outgoing
`_scope` and remembers correctly. It only ever broke the case that mattered most: leaving a real menu
and coming back to whatever was behind it.

The fix is to remember from inside `OnPanelChanged` itself, before it clears anything - the last
point where the outgoing scope and selection are both still valid.

## The bare HUD's default selection is the bottom bar's leftmost tool, not top-left

`SpatialNavigator.First` picks the top-left of the biggest on-screen cluster, which is a reasonable
guess for a menu but not for the Game or MapEditor HUD: the player's most common next action is
picking up the tool they already had out, which sits at the *left of the bottom bar*, nowhere near
the top of the screen.

`BottomBarNavigation.DefaultTool` looks for a `MainSection` ancestor among the current candidates and
returns its leftmost child if found, falling through to `SpatialNavigator.First` otherwise. It needs
no scene check to stay out of menus and dialogs: a stacked panel's own scope element never contains
the HUD's `MainSection` in the first place, so the lookup simply comes back empty everywhere but the
bare HUD. Wired in as a fallback ahead of `SpatialNavigator.First`, after `SelectionMemory.Restore`,
in both `EnterScope` and `TryInitialSelection` - real memory of where the player actually was still
wins over this default.

## A closed tool group row leaves the selection pointing at nothing

Confirming a bottom bar category (`ToolGroupButton`) calls `ToolGroupService.EnterToolGroup`, which
shows that category's row via `ToolButtonsElement.ToggleDisplayStyle(true)` - `TrySubSectionJump`
jumps the selection onto its leftmost tool. But the row can close again through paths this mod never
sees: pressing B falls through to `GamepadToolCancelInputProcessor`, which calls
`ToolGroupService.ExitToolGroup()` directly; placing a building can close it too. Either way
`ToolButtonsElement.ToggleDisplayStyle(false)` runs entirely inside the game's own event handler
(`ToolGroupButton.OnToolGroupExited`), and nothing tells this mod it happened. Left alone, `_selected`
kept pointing at a tool button sitting in a hidden row - confirming it dispatched a click nothing could
receive, and `Move` would only notice once the stick was actually pushed, since `RefreshCandidates`
only runs on a step.

The fix tracks two things across the jump into a row: `_activeToolGroupRow`, the specific
`ToolButtonsElement` a landed-in tool belongs to (found as the direct child of the shared `SubSection`
container that contains it - not `SubSection` itself, which never goes `display: none`, only the
per-category children inside it do), and `_activeToolGroupOwner`, the category button that opened it.
`TryRecoverFromClosedToolGroup` reads `_activeToolGroupRow.resolvedStyle.display` every frame - one
field read, not a candidate walk - and the moment it goes `None` while the selection is still inside
that row, sends the player back to the category button that opened it, exactly where B would otherwise
have left them stranded.

## Environment note

The dev machine's controller is a Steam Controller where Steam Input can't be disabled. It only
appears as `Gamepad.current` (XInput) when its Steam Input layout is a **gamepad-style template**;
a desktop/mouse layout makes the sticks emit mouse movement and `Gamepad.current` stays null.
