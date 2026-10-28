# Theme presets

The launcher reads UTF-8 theme files from `themes/` beside `CMCLauncher.exe`. Source palettes live in `winui/themes/`.

Fresh green (`craft`) is the only embedded color preset. Its external `craft.json` can override the embedded colors. If that file is missing or invalid, the embedded JSON remains available. Lake Cyan (`lakecyan.json`) and Sakura (`sakura.json`) are distributed as external palettes and may be edited or removed. The WinUI3 native preset continues to use Windows theme resources; it is never loaded from JSON. The Custom color editor and saved appearance profiles remain separate.

## Editing a palette

Change the source JSON and rebuild, or edit a file beside the executable and restart the launcher or reopen Personalization. The latter reloads palettes without recompilation. Switching between light, dark and system appearance continues to work with each palette.

```json
{
  "version": 1,
  "id": "ocean",
  "displayName": "Ocean",
  "colors": {
    "LightBg": "#EDF4F6",
    "LightAccent": "#3B8998"
  },
  "light": {},
  "dark": {
    "DarkBg": "#172427",
    "DarkAccent": "#6AB8C7"
  }
}
```

The filename must match the ID: `ocean.json` for `ocean`. IDs use lowercase letters, numbers, underscores or hyphens, start with a letter, and are at most 64 characters. `native` and `custom` are reserved. Additional valid files automatically appear in the preset selector. Display names use English by default; built-in translation entries also apply to known names.

`colors` maps existing color token names to `#RRGGBB` values. The shipped files list the available tokens, including `LightBg`, `DarkBg`, `LightSidebar`, `DarkSidebar`, `White`, `DarkSurface`, text, muted, accent, selection, hover, pressed and border variants. `light` and `dark` override individual tokens in their respective appearance modes. Keep the token names unchanged. Missing colors fall back to Fresh green; tokens unknown to a palette keep the control's source color if no fallback exists.

An external override of `craft` can contain only the entries it changes. Invalid metadata, reserved IDs, mismatched filenames, unsupported schema versions, malformed colors and files larger than 256 KB are ignored and diagnosed in runtime logs. A broken file does not prevent startup.

## Packaging

Normal builds and publishes copy `themes/*.json` into the output directory. Single-file builds include these files in their companion resources. Only `craft.json` is embedded as the built-in fallback.

Saved appearance profiles keep their existing `colorPreset` IDs and format. To import or restore a profile that references an external palette, install the matching theme JSON first. Selecting WinUI3 native or Fresh green restores the default font and scaling; external palettes preserve the user's typography and scaling.
