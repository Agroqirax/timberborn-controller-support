# Timberborn modding notes (v1.1.2.0)

Reference notes gathered while building this mod, so the basics don't have to be
re-discovered. Decompiled game source (AssetRipper) lives at
`~/Documents/timberborn-exports/1.1.2.0-cf8e8d1-xsw/Scripts/`, one folder per assembly.

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

## Environment note

The dev machine's controller is a Steam Controller where Steam Input can't be disabled. It only
appears as `Gamepad.current` (XInput) when its Steam Input layout is a **gamepad-style template**;
a desktop/mouse layout makes the sticks emit mouse movement and `Gamepad.current` stays null.
