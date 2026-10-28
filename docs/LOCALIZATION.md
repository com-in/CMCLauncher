# Interface language packs

The launcher reads UTF-8 JSON files from `locales/` beside `CMCLauncher.exe`. The source files are in `winui/locales/`. These interface translations are separate from Minecraft's game language and the Windows MUI files under `languages/`.

New installations default to English. Explicitly saved language preferences are preserved. The `strings` keys are English; Chinese text lives in the Chinese pack values. The English pack's `aliases` map supports legacy source messages.

FRP-specific interface strings intentionally use the English fallback and are omitted from the Chinese pack. Shared actions retain their existing translations. New development uses English resource keys and English default text.

## Editing translations

Edit `locales/en_us.json` or `locales/zh_cn.json`. Restart the launcher, or switch interface languages, to reload the files. No compilation is required for external translation edits.

```json
{
  "code": "en_us",
  "displayName": "English",
  "strings": {
    "Save": "Save",
    "Page {0} of {1}": "Page {0} of {1}"
  }
}
```

Keep keys unchanged when translating values. Keys use English text; `context.*` keys distinguish uses that require different translations. Parameterized messages use .NET composite formatting: retain `{0}`, `{1}`, and format specifiers such as `{0:N0}`. Parameters may be reordered, but their indexes must remain valid. Literal braces in formatted messages use `{{` and `}}`.

External entries override the built-in JSON copy. Missing entries in the bundled Chinese or English packs retain their bundled values. Missing entries in additional languages fall back to English, then to the original text if no key exists. Malformed files are ignored and recorded in the runtime log. Invalid formatting falls back to the built-in English template. The built-in copies also let the launcher start if the external files are missing.

## Adding a language

Copy a pack to a file such as `locales/fr_fr.json`, set its `code` to `fr_fr`, and set `displayName` to the language's name. The filename and code must match and use lowercase `xx_yy`. Valid language packs appear automatically in the interface language selector after a restart or language reload. A partial pack is supported through the fallback above; translating the whole `strings` collection gives more complete coverage.

## Dynamic compatibility rules

The English pack also contains ordered `prefixes` and `rules` for older dynamic status messages. Exact string entries take precedence, followed by regex rules and then prefix replacements. Regex rules may define capture translations and replacements. Patterns run with a timeout; malformed patterns invalidate the external pack. New parameterized UI messages should use `UiFormat(key, values)` and `strings` templates rather than adding more regex rules.

The launcher does not translate project descriptions, user configuration contents, or raw third-party output such as frpc logs. Native Windows file picker text is controlled by Windows, not these JSON packs.
