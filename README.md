# Input Device Interceptor

A Windows app for seeing what an input device sends, and changing what it does. It was built for a Bluetooth
scroller ring (a small ring with buttons and a touch surface), but it works with any keyboard, mouse, media-key,
touch, gamepad or telephony HID device.

## Features

### Device list

- Lists paired Bluetooth devices first (Classic and LE entries of the same device are merged), with their
  connection state, followed by every input collection Windows reports.
- Each entry has an event counter. If you don't know which entry is your device, press one of its buttons and see
  which counter goes up.

### Live input inspector

- Shows the last button pressed and what Windows does with it by default (for example "Volume up" or "Scroll").
- The **Live log** tab lists every event from the selected device: time, action, button name, HID code, default
  Windows behavior, which collection sent it, and the raw report bytes.
- Filters:
  - **Applied actions only** keeps only the inputs that make Windows do something (volume, media, scroll, swipe,
    tap…).
  - **Show releases & repeats** includes key-up and auto-repeat events.
  - **Record movement** logs mouse and touch movement.
- **Copy log** copies the visible rows as tab-separated text, so you can paste them into a spreadsheet.

### Mappings

A mapping says "when this device sends this input, do this instead". Mappings run in the background, even when the
window isn't focused.

- **Triggers:** keys, media keys and other HID usages, mouse buttons, the mouse wheel (vertical and horizontal),
  touch taps and swipes (up, down, left, right), and mouse drags.
- **Actions:**
  - **Scroll up / down** by a chosen number of notches (1 notch ≈ 3 lines). Scrolling goes to the window under the
    mouse pointer.
  - **Keyboard shortcut**: any key with Ctrl, Alt, Shift and Win modifiers. It goes to the focused app.
  - **Block only**: do nothing, just stop the default action.
- **Block default:** stops the input's normal Windows action for keys and media keys (volume, play/pause…). Only
  the mapped device is blocked; the same key from your normal keyboard still works.
- A mapping can target a whole Bluetooth device or a single input collection.
- **Map last input** creates a mapping for whatever you just pressed or swiped. **Add mapping** creates one from a
  list of triggers.
- **Mappings active** turns every mapping off at once without deleting them.

### Touch blocking

Windows acts on a touch surface directly, so key blocking can't stop it. Instead the app can hide the device's touch
channel from Windows with [HidHide](https://github.com/nefarius/HidHide), a free filter driver.

- **Block touch from Windows** hides the touch collection and allows this app to keep reading it. This asks for
  administrator approval once.
- **Read touch for mappings while blocked** keeps touch mappings working after blocking. Untick it to block touch
  without the app reading the touch channel at all.
- Unblocking restores the device. If the app turned HidHide's global hiding on, it turns it back off.
- If HidHide isn't installed, the button links to its download page.

## Requirements

- Windows 10 (version 2004) or later
- [.NET 10 SDK](https://dotnet.microsoft.com/download) to build
- [HidHide](https://github.com/nefarius/HidHide/releases/latest), only for touch blocking

## Build and run

```sh
dotnet run
```

To make a single self-contained exe:

```sh
dotnet publish InputDeviceInterceptor.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

## Settings

Settings are saved in `%AppData%\InputDeviceInterceptor\`:

- `mappings.json`: your mapping rules
- `touch-block.json`: which touch collections are blocked
