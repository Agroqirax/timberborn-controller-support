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
   quantise the stick to one cardinal axis, then pick the candidate with the smallest
   `advance + crossAxisGap * penalty`, where advance is the centre-to-centre distance along the
   travel direction and the gap is measured edge-to-edge so anything sharing your row or column
   scores zero. (Stepping an index through the candidate list in *tree order* — an earlier attempt —
   never dead-ends either, but makes left/right and up/down do the same thing and wraps the
   selection into unrelated parts of the panel.)
4. **Focus is tracked per composite root.** `FocusController` keeps a focused element per subtree,
   so `focusController.focusedElement` never gives one coherent "current element" across panels,
   and `FocusOutEvent`-based highlight cleanup leaves stale highlights behind. Track selection in
   the mod instead.
5. **No visible `:focus` styling** in Timberborn's theme, but `:hover` is styled. Reusing the hover
   pseudo-state makes a controller selection look native. `VisualElement.pseudoStates` and the
   `PseudoStates` enum (`Hover = 2`) are `internal` → reflection required.
6. `ScrollView.ScrollTo(VisualElement)` is public — use it to keep the selection on screen inside
   scrolling panels — but it **throws** `ArgumentException` unless the element is inside that
   ScrollView's `contentContainer`. A `Scroller` (the scrollbar) holds `RepeatButton`s and a dragger
   that are descendants of the ScrollView but *not* of its content container, so a naive
   "nearest ScrollView ancestor" walk both crashes and produces ghost selections. Check
   `scrollView.contentContainer.Contains(element)` first, and skip anything under a `Scroller`.
7. Detecting "is this element clickable" generically: reflect `VisualElement.m_CallbackRegistry` →
   `m_BubbleUpCallbacks` → `m_Callbacks` (`m_Array`/`m_Count`) and compare each functor's public
   `eventTypeId` field against `EventBase<ClickEvent>.TypeId()` (which is public).

## Environment note

The dev machine's controller is a Steam Controller where Steam Input can't be disabled. It only
appears as `Gamepad.current` (XInput) when its Steam Input layout is a **gamepad-style template**;
a desktop/mouse layout makes the sticks emit mouse movement and `Gamepad.current` stays null.
