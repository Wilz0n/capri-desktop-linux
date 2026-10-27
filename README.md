<p align="center">
  <img src="assets/chip-source.png" alt="Chip" width="40%" align="middle">
  <img src="assets/capri.png" alt="Capri" width="40%" align="middle">
</p>

<p align="center">
  Bring Capri (back lol) to life as a desktop companion! Inspired by jpartnma_'s tiktok video.
</p>

<p align="center">
  <a href="https://github.com/lachydotmcg/capri-desktop/releases/latest/download/Capri-Setup.exe"><b>Download for Windows</b></a>
  &nbsp;·&nbsp;
  <a href="#mac-and-linux-early-preview">Mac and Linux</a>
  &nbsp;·&nbsp;
  <a href="https://lachydotmcg.github.io/capri-desktop/setup/">Setup guide</a>
  &nbsp;·&nbsp;
  <a href="https://lachydotmcg.github.io/capri-desktop/">Website</a>
  &nbsp;·&nbsp;
  <a href="https://discord.gg/F4fDHfGtFk">Discord</a>
</p>

<table align="center">
  <tr>
    <td align="center"><img src="assets/readme/idle.webp" alt="Capri idling and blinking" width="150"><br><sub>Hanging out</sub></td>
    <td align="center"><img src="assets/readme/talk.webp" alt="Capri talking" width="150"><br><sub>Talking</sub></td>
    <td align="center"><img src="assets/readme/tossed.webp" alt="Capri flailing while dragged" width="150"><br><sub>Getting tossed</sub></td>
    <td align="center"><img src="assets/readme/glitch.webp" alt="Capri glitching" width="150"><br><sub>Glitching</sub></td>
    <td align="center"><img src="assets/readme/sleep.webp" alt="Capri asleep, then booting back up" width="150"><br><sub>Napping</sub></td>
  </tr>
</table>

Beware that Capri may try to take over your human CPU. We take no responsibility for her actions.

## What she does

- **Talks back.** Type to her and she answers in her own words.
- **Talks out loud.** Add an ElevenLabs key and she speaks, or voice chat with her.
- **Does things for you.** Ask her to open apps, play music or close a tab.
- **Watches you play.** Turn it on and she comments on your games.
- **Sits on your stuff.** Drop her on a window, an icon or the bottom of your screen.

<p align="center">
  <img src="assets/readme/bubble.png" alt="Capri's speech box with the pencil and wave buttons" width="31%">
  <img src="assets/readme/sit.png" alt="Capri sitting on a window" width="31%">
  <img src="assets/readme/mainframe.png" alt="The Capri Mainframe" width="22%">
</p>

## What you need

- Windows 10 or 11 (64-bit). Nothing else to install.
- A mic, if you want to voice chat.

| | Brain on your PC | Brain from a key (like the free NVIDIA one) |
| --- | --- | --- |
| Memory | 8 GB, 16 GB is better | 4 GB |
| Graphics card | Optional, but 4 GB or more makes her faster | Not needed |
| Space | 3 to 5 GB | Under 100 MB |
| Internet | Only for the download | Always |

## Getting started

1. Download **Capri-Setup.exe** and run it
2. In the Capri Mainframe, press **Set up her brain**, or press **Free key** to use a free NVIDIA key
3. Optional: paste an ElevenLabs key so she can talk. Make the key with **Restrict key** turned off

Stuck? The [setup guide](https://lachydotmcg.github.io/capri-desktop/setup/) walks through it, or ask in the [Discord](https://discord.gg/F4fDHfGtFk).

Windows or your antivirus may warn you because it's a new app. All the code is here.

## Mac and Linux (early preview)

Mac and the upstream Linux release remain early previews. The Linux/Hyprland setup described below is the development setup used for Capri's Linux voice, OpenRouter Brain and desktop integration; it requires the Linux Capri build from this repository rather than the older upstream tarball.

1. Download [Mac (Apple Silicon)](https://github.com/lachydotmcg/capri-desktop/releases/latest/download/Capri-mac-apple-silicon.zip), [Mac (Intel)](https://github.com/lachydotmcg/capri-desktop/releases/latest/download/Capri-mac-intel.zip) or [Linux](https://github.com/lachydotmcg/capri-desktop/releases/latest/download/Capri-linux-x64.tar.gz)
2. Install [Ollama](https://ollama.com), then run `ollama pull gemma4:e4b` for her brain
3. **Mac:** unzip it and open Capri. If macOS says it can't check her, open System Settings → Privacy & Security and click **Open Anyway**. Needs macOS 15 or newer
4. **Linux upstream preview:** unpack it and run `Capri/Capri`. It sits on windows on X11; on Wayland it sits at the bottom of your screen. The additional Linux features below are in this repository's Linux build.

### Linux/Hyprland setup and migration

This section describes the current development configuration so the Linux setup can be recreated on another computer. It is not a one-click installer: copy the repository/build and configure each machine's compositor and services locally.

#### Requirements

- 64-bit Linux, .NET 10 SDK to build from source, and a Wayland session using Hyprland 0.55+ (the current development setup uses Hyprland 0.56.2).
- Voxtype installed as a **user** service, configured with a working microphone and Whisper model. The current setup uses local Whisper `large-v3-turbo`, Spanish (`es`), a `type` output mode, and an enabled Voxtype state file. Choose/download the Whisper model on the destination machine as appropriate.
- `playerctl` and `wpctl` for the optional Linux media controls; `notify-send` for recording status notices.
- A mic and internet access for local transcription input plus the cloud Brain. OpenRouter is optional if only local dictation is needed.

#### Build and launch Capri

From the repository root:

```sh
dotnet build cross/Capri.Cross.csproj -c Release
dotnet publish cross/Capri.Cross.csproj -c Release -r linux-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:DebugType=none -o out/Capri
```

Launch `out/Capri/Capri`. Keep the executable and its resources together when not using single-file publishing. Capri stores ordinary preferences in its user config folder and the OpenRouter key separately in `$XDG_CONFIG_HOME/Capri/openrouter.key` (normally `~/.config/Capri/openrouter.key`, mode `0600`). Do not commit or copy that key in a repository, `cross.json`, logs, screenshots, or this guide. On a new device, open the Capri tray menu → **Brain settings…** and enter that device's own key.

#### Brain: OpenRouter free-only setup

1. Open **Brain settings…** from Capri's tray menu.
2. Enter an OpenRouter API key, select a currently supported `:free` model, choose a reply language, and press **Connect**. The key is stored locally with owner-only file permissions.
3. The configured model is checked against Capri's built-in free-model allowlist before use. Ordered model IDs retain the `:free` suffix; no local paid-model fallback is used. `openrouter/free` is an optional standalone mode and is not mixed into the ordered model list.
4. Verify the panel shows **Connected** and send one ordinary, non-private prompt. A tool action is only successful after Capri executes it; do not assume that model text alone means an action happened.

Free model availability and limits can change. A `:free` model may be temporarily rate-limited or unavailable; wait and retry rather than substituting a paid model. Never paste a key into a terminal command or ask an assistant to print it.

#### Voice: Capri Wave button vs. global Voxtype hotkey

These are deliberately separate paths:

- **Capri's Wave button:** Capri checks Voxtype's state, starts a recording to `$XDG_RUNTIME_DIR/capri/voice.txt` with directory mode `0700`, stops only a recording it started, waits for transcription, consumes the text once, then sends it through Capri's normal safety/media/Brain pipeline. The on-screen Wave changes to listening/transcribing. If OpenRouter is not connected, Capri reports the recognized text and asks for Brain setup rather than claiming it answered.
- **Global Voxtype shortcut:** in the current personal Hyprland setup, `Super + Ctrl + G` is the normal standalone `voxtype record toggle`. It types the recognized text into whichever field currently has focus. It does **not** send that text to Capri. There is intentionally no paired `Super + Alt + G` binding in this setup; the toggle key starts or stops based on Voxtype's state. If a destination device uses a separate start/stop pair, configure it according to that device's Voxtype documentation, keeping it separate from Capri's Wave flow.

Voxtype settings are machine-specific and intentionally are not modified by Capri's installer. Confirm the daemon is active (`systemctl --user status voxtype.service`) and the state file is `idle` before testing. Test global typing in a harmless text field first; test the Wave button separately. Do not trigger a hotkey while another Voxtype recording is active: Capri's button will not stop a recording it did not start.

The Wave button uses Voxtype's CLI directly. Its output goes to Capri's private runtime inbox, and Capri polls the state file to avoid consuming partial or duplicate transcripts. The runtime inbox is temporary and is not a migration target; create it automatically on the new session. Do not copy another computer's `/run/user/...` files.

#### Hyprland: hidden border for Capri only

For Hyprland 0.55+ (Lua configuration), this persistent per-window rule hides only Capri's compositor border while preserving global border settings and normal input:

### Capri without Hyprland's window outline

On Hyprland 0.55+ (Lua configuration), you can hide the compositor border for Capri only while keeping the normal border width and colors for all other windows. Add this to `~/.config/hypr/hyprland.lua` or to a Lua file it loads:

```lua
hl.window_rule({ name = "capri-no-outline", match = { class = "^Capri$" }, border_size = 0 })
```

Add the rule to `~/.config/hypr/hyprland.lua` or a Lua module it loads, then run `hyprctl reload`. The anchored class match is intentionally scoped; do not set transparent border colors or `border_size = 0` globally. Check the actual class on a new machine with `hyprctl clients -j` and adjust the regex only if that class differs. The rule persists across Capri restarts.

For Capri's rescue binding, the personal Hyprland setup uses `Super + Shift + C` → `capri --rescue`. It only works when the running Capri process has the rescue IPC listener. If Capri is off-screen, check `hyprctl clients -j` and the registered binding with `hyprctl binds -j`; do not blindly move or close other windows. Use the running process's tray action/menu or the rescue helper, and verify the resulting position afterward.

#### What to migrate (and what not to)

- Migrate the Capri source/build instructions, the destination device's personal Hyprland Lua rule/bindings, and a freshly configured Voxtype service/model.
- Re-enter the OpenRouter key on the destination through Capri's Brain settings; do not copy secrets as part of source control or documentation.
- Do **not** copy runtime state such as `$XDG_RUNTIME_DIR/voxtype/state` or `$XDG_RUNTIME_DIR/capri/voice.txt`.
- Do **not** overwrite the destination's `~/.config/voxtype/config.toml` or all of `~/.config/hypr`; add only the needed personal bindings/rule.
- `plan.md` in this development checkout is a local planning file, not a portable setup guide. This README section is the human-facing migration note.

## How to use her

- **Pencil:** type to her
- **Wave:** voice chat
- **Drag her:** sit her on a window
- **Right-click her** or the tray icon: menu and settings

Just ask her for things, like "open Spotify", "play Blinding Lights", "pause", "what's playing?" or "close Discord".

<details>
<summary>All controls</summary>

| Control | What it does |
| --- | --- |
| Shift + drop | sit her right where you drop her |
| Ctrl + drop | she hangs on; click her to let go |
| Mic button | mute your mic in voice chat |
| Ctrl + Alt + V | start or stop voice chat |
| Ctrl + Alt + N | mute your mic |
| Ctrl + Alt + C | get her off your cursor |
| Right-click, Move | she hops between your open windows |
| Watch my screen | in the Mainframe: she comments as you play |
| Speak replies | in the Mainframe: turn off to save ElevenLabs credits |

</details>

## Coming up

- Working with your files: open, read and edit the ones you pick
- Controlling YouTube and your apps: skip, fullscreen, click, type and scroll
- A free voice that runs on your PC
- Remembering things about you
- Looking things up and telling you the answer
- Her own cursor that she moves around your screen
- Head pats (smh)
- Mischief mode: screen glitches, stealing your cursor, "you've been hacked" notes
- Mac and Linux: her voice, the Mainframe and doing things for you

## Licence

Free for anything non-commercial: use her, share her, change her. No selling her or putting her in paid products. Full terms in [LICENSE.md](LICENSE.md) (PolyForm Noncommercial 1.0.0).

Have fun and more updates to come!

<sub>Setup guide based on Dancer's guide in the Discord. Font: Share Tech Mono (OFL).</sub>
