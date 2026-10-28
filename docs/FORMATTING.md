# Code formatting

- C#: CSharpier 1.2.1, configured in `.csharpierrc.json`. Run `dotnet tool restore`, then `dotnet csharpier format .`. Use `dotnet csharpier check .` to check formatting without changing files.
- Website, JSON and YAML: Prettier 3.6.2, configured in `.prettierrc.json`.
- XAML, project files and manifests: Prettier with `@prettier/plugin-xml` 3.4.2. Preserve XML text whitespace and put attributes on separate lines.
- PowerShell: PSScriptAnalyzer 1.25.0, configured in `.PSScriptAnalyzerSettings.psd1`. Use `Invoke-Formatter -ScriptDefinition (Get-Content -Raw <file>) -Settings ./.PSScriptAnalyzerSettings.psd1` and save the result as UTF-8 with BOM for Windows PowerShell compatibility.

`.editorconfig` supplies indentation and line endings for editors. Format source files only; build outputs, generated sources, release packages, IDE state and test fixtures are excluded.
