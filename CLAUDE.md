# DRS Unreal Tools: notes for working on the repo

The READMEs are for users of the tools. Building and releasing are in the root `README.md`; developer-only details
go here.

## UE Test Tool icon

`Tools/UETestTool/Assets/UETestTool.ico` is generated; don't edit it by hand. Regenerate it after changing the
artwork or the script:

```powershell
powershell -ExecutionPolicy Bypass -File Tools\UETestTool\Assets\make-icon.ps1
```

- **Source:** `Assets/IconSource/UE_Test_Tool_Icon_Master.png` (1024 x 1024). `UE_Test_Tool_Icon_Layered.svg` is the
  same artwork as an embedded PNG, plus a hidden vector draft that doesn't exactly match it; the script doesn't use it.
- **Transparency:** the master is a rounded tile on solid black, with blue glow above and below. The script makes the
  black transparent, so the icon doesn't show as a black square on light backgrounds: anything outside the tile's
  silver frame gets its opacity from its brightness, which keeps the glow as translucent light. Inside the frame stays
  opaque.
- **Two crops:**
  - 256, 128, 96, 72 and 64 px (Explorer's larger views, Start, Alt+Tab): the tile, cropped so it fills the icon.
  - 48 px and below (taskbar, title bar, Explorer lists): just the ring and play triangle, cut out as a circle, so the
    mark is about a third bigger where the icon is small.
- **Why the cut-over is 48 px:** at 150% display scaling, WPF gives Windows the 48 px frame as the window's main icon,
  and the taskbar shrinks that rather than using a smaller frame. With the cut-over at 32 px, the taskbar showed the
  tile.
- **Sizes:** 16/20/24/32/48 for 100% scaling, plus 30/40 (125%) and 36/72 (150%), so Windows rarely has to
  rescale a frame (which blurs it).
- **Settings** at the top of the script: the crop rectangles, the ring's centre and radius, the cut-over and the
  sizes. They're measured from the current master, so check them if the artwork's layout changes.
- Every frame is stored PNG-compressed. 48 px and up are scaled straight from the 1024 px artwork, smaller ones from a
  256 px rendering of the ring.

To check the result, look at the frames on dark and light backgrounds, and at the built app's title bar.
