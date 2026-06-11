# RKNZ Blender Render

A Windows batch render queue manager for Blender — queue multiple `.blend` files and render them sequentially, hands-free.

> **Version:** 1.0.0 &nbsp;•&nbsp; **Developer:** [Rikokenz](https://github.com/rikokenz) &nbsp;•&nbsp; **License:** GPL v3.0

---

## Features

- **Batch render queue** — add as many `.blend` files as you need; they render one after another
- **Custom frame ranges** — override per-job start/end frames, or leave blank to use the scene timeline
- **Custom output paths** — redirect rendered frames to any folder, or keep the `.blend`'s built-in default
- **Render time estimation** — samples one frame per scene to produce a seconds-per-frame estimate and total time forecast before the full render starts
- **Drag-to-reorder** — rearrange jobs by dragging rows up or down in the queue
- **Live terminal output** — a dedicated terminal window streams every line Blender prints
- **Stop / resume** — cancel at any time; already-completed jobs are skipped on the next run
- **When Done action** — choose what happens after the queue finishes (e.g. do nothing, shut down)
- **Light / Dark theme** — switch in Preferences; applied instantly across all open windows
- **Persistent settings** — Blender path and theme saved to `%AppData%\BlenderTool\config.json`

---

## Requirements

| Requirement | Details |
|---|---|
| OS | Windows 10 or later |
| Blender | Any version supporting `-b` headless rendering (tested on 4.x and 5.x) |
| .NET Runtime | .NET 6 or later (Windows Desktop Runtime) |

---

## Installation

### Option A — Pre-built release *(Private Club tier)*

The compiled, ready-to-use `.exe` is available exclusively to **Private Club** members on Patreon — the higher membership tier.

👉 **[Join the Private Club on Patreon](https://www.patreon.com/rikokensfw)**

Once you're a Private Club member, find the download link in the members-only post.

### Option B — Build from source *(free)*

1. Clone or download this repository.
2. Open the solution in Visual Studio 2022 with the **.NET Desktop** workload installed.
3. Build in **Release** configuration (`Ctrl+Shift+B`).
4. The executable is output to `bin\Release\net6.0-windows\`.

---

## First-Time Setup

Before rendering, tell the app where Blender is installed:

1. Click **Preferences** in the menu bar.
2. Click **Browse…** and navigate to `blender.exe`  
   *(e.g. `C:\Program Files\Blender Foundation\Blender 4.x\blender.exe`)*
3. Optionally change the theme.
4. Click **Save**.

The path is remembered on next launch.

---

## How to Use

### Adding jobs

1. Click **Browse…** next to the blend file field and select a `.blend` file.
2. Optionally enter **Frame Start** and **Frame End**. Leave both blank to render the full scene timeline.
3. Click **Add to Queue**. Repeat for additional files. Drag rows to reorder.

### Estimating render time

- Click **Calc. Est.** to render one sample frame per unique blend file in the queue.
- The **Est. Time** column and **Total Est.** footer update with projected durations.
- Estimates continue updating in real time as the full render progresses.

### Rendering

- Click **Process Queue** to begin. Jobs execute in order from top to bottom.
- Click **Stop** at any time to cancel. The running Blender process is terminated immediately.
- Jobs marked **Done** are skipped automatically on subsequent runs.

### Terminal output

- Click **Log** to open the terminal window — it streams every line Blender prints.
- Click **Clear** inside the terminal to wipe the log.
- Closing the terminal hides it; the log is preserved until cleared or the app is closed.

---

## Queue Columns

| Column | Description |
|---|---|
| # | Job order in the queue |
| File | Full path to the `.blend` file |
| Frames | Frame range override, or *Full timeline* if none was set |
| Output | Destination folder for rendered frames, or *(default)* |
| Est. Time | Projected render time based on the sampled seconds-per-frame |
| Status | Waiting / Rendering… / Done / Failed |

---

## Configuration File

Settings are stored at:

```
%AppData%\BlenderTool\config.json
```

Example:

```json
{
  "BlenderPath": "C:\\Program Files\\Blender Foundation\\Blender 4.x\\blender.exe",
  "Theme": "Dark"
}
```

You can edit this file directly, but using **Preferences** in the app is recommended.

---

## License

This project is licensed under the **GNU General Public License v3.0** — free to use, modify, and distribute. Any modified version must also be open-source under GPL v3. No warranty is provided. See [LICENSE](LICENSE) for the full text.

---

## Support

If you find this tool useful, consider supporting development:

☕ [Support me on Patreon](https://www.patreon.com/rikokensfw)
