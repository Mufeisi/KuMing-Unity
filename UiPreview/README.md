# MonoGame procedural HUD preview

This standalone preview demonstrates a dark fantasy game-client GUI rendered entirely by MonoGame.
It has no PNG, XNB, shader, or font assets: panels, icons, orbs, the dungeon backdrop, and the pixel
font are generated in code at runtime.

## Run

```powershell
dotnet run --project .\UiPreview\UiPreview.csproj
```

- `C` toggles the character panel.
- `Tab` toggles chat.
- Clicking a skill slot selects it.
- Hovering a skill slot shows a tooltip.
- `Esc` exits.

## Capture a deterministic preview

```powershell
dotnet run --project .\UiPreview\UiPreview.csproj -- --capture .\Build\MonoGameUiPreview.png
```
