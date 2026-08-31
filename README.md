# NovaPresentationFramework

Run stock WPF (`System.Windows.*`) applications **natively on Linux** — a managed
replacement for the MIL/DUCE rendering channel, an SDL3 `PresentationSource`, and a
Vulkan raster backend. No Wine, no WpfGfx, no Windows.

> [!WARNING]
> **Experimental and not production ready.** This project is completely an AI-slop codebase. Please do not rely on it being correct nor secure for a production grade deployment.

---

## What it is

This repository does **not** fork `dotnet/wpf` and does **not** vendor `WpfGfx`
/ milcore. Consumers write against stock `PresentationFramework` /
`PresentationCore` / `WindowsBase` assemblies. This project supplies:

1. A managed replacement for the MIL/DUCE native ABI (`wpfgfx_cor3`).
2. An SDL3 `PresentationSource` + `CompositionTarget` (`Nova.SdlSource`).
3. A raster backend on Vulkan (`Nova.Vulkan`) with SDL3 WSI. The software path
   is the **same binary** pinned to Mesa lavapipe (`VK_DRIVER_FILES`).
4. Tiny, upstream-shaped patches only where a `DllImportResolver` or a public
   subclass cannot intercept (see `patches/`).

### What it is not

- Not Avalonia XPF (commercial WPF fork).
- Not Wine / wine-mono (PE `wpfgfx_cor3.dll` inside a Win32 process).
- Not a new XAML dialect. Public types stay `System.Windows.*`.

## How it fits

```
app (net10.0 / net11.0, not -windows)
  → stock PresentationFramework / PresentationCore / WindowsBase
      → DUCE.Channel P/Invokes          ──► this repo: managed MIL slave
      → HwndSource / user32             ──► this repo: SdlPresentationSource
      → DWriteLoader / dwrite.dll       ──► this repo: HarfBuzz + FreeType
      → WIC / windowscodecs             ──► this repo: ImageSharp
          → Nova.Vulkan device (SDL3 WSI)
              → Vulkan ICD: GPU  |  lavapipe (VK_DRIVER_FILES)
```

Do not compile `wpfgfx.vcxproj` — the native tree is D3D9 + HWND + DWrite + WIC +
GDI present; replacing it is a Wine rewrite.

## Contributing

Pull requests welcome. The two rules that keep the tree sound:

1. `patches/verify-series.sh` must pass (`MATCH=N DIFFER=0`) — any edit under
   `third_party/dotnet-wpf` must be mirrored into the patch series, and never
   made directly against the submodule tree alone (see `patches/README.md`).
2. The 19 suites + harness must stay green; run them sequentially with
   `SDL_VIDEODRIVER=offscreen` (the bootstrap `--tests --harness` does this).

This is an experiment-first codebase: verification claims must be reproducible,
not asserted.

## License

- This project: **MIT** ([LICENSE](LICENSE)).
- The consumed upstream `dotnet/wpf` assemblies are MIT
  (© .NET Foundation and Contributors); `LICENSE.TXT` ships with every package.
- Third-party attributions and companion licenses (LibTessDotNet under SGI
  Free Software License B, the derived icon font under Apache-2.0/MIT,
  ImageSharp under the Six Labors Split License — Apache-2.0 for OSS
  consumers): see [NOTICE](NOTICE).
