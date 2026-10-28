<p align="center">
  <img src="assets/chip-source.png" alt="Chip" width="40%" align="middle">
  <img src="assets/capri.png" alt="Capri" width="40%" align="middle">
</p>

<p align="center">Bring Capri (back lol) to life as a desktop companion!</p>

<p align="center">
  <a href="https://github.com/lachydotmcg/capri-desktop/releases/latest/download/Capri-Setup.exe"><b>Download for Windows</b></a>
  &nbsp;·&nbsp; <a href="#linux-and-omarchy">Linux / Omarchy</a>
  &nbsp;·&nbsp; <a href="https://lachydotmcg.github.io/capri-desktop/setup/">Setup guide</a>
  &nbsp;·&nbsp; <a href="https://discord.gg/F4fDHfGtFk">Discord</a>
</p>

## What she does

- **Talks back.** Type to her and she answers in her own words.
- **Talks out loud.** Add an ElevenLabs key for spoken replies.
- **Listens when you click Wave.** Local Voxtype transcription; no global dictation hotkey is needed.
- **Does things for you.** Ask her to open apps, play music or close a tab.
- **Sits on your stuff.** Drop her on a window, an icon or the bottom of your screen.

## Linux and Omarchy

The `linux-features` branch contains Capri's current Linux/Hyprland implementation: Gemini and OpenRouter Brain providers, Voxtype voice capture, Omarchy Super+I app registration, a per-window border rule and the Super+Shift+C rescue binding. Use this branch and its build; the upstream Linux tarball does not include all of these integrations.

### Install on another Omarchy machine

First clone the current branch. Review the dry-run output before installing:

```sh
git clone --branch linux-features --single-branch https://github.com/Wilz0n/capri-desktop-linux.git
cd capri-desktop-linux
git log -1 --oneline
./setup-omarchy.sh --dry-run
```

If the plan looks right, install the packages, configure Voxtype, publish Capri and register the menu. To also append the scoped Hyprland border and rescue rules (with backups), use:

```sh
./setup-omarchy.sh --apply-hyprland
```

Installer options:

| Option | Effect |
| --- | --- |
| `--dry-run` | Preview actions without installing packages or editing config. |
| `--apply-hyprland` | Add only Capri's Lua border rule and Super+Shift+C rescue bind, then reload Hyprland. |
| `--skip-voxtype` | Skip Voxtype package/service/model setup. |
| `--skip-build` | Reuse an existing `out/Capri/Capri` publish. |
| `--help` | Show all options. |

The installer is intended for Arch/Omarchy in an active Wayland/Hyprland session. It installs the .NET 10 SDK and Linux dependencies, installs `voxtype-bin` from AUR if needed, downloads/activates Whisper `large-v3-turbo` for Spanish, disables Voxtype's global hotkey, publishes a self-contained Linux binary, creates `~/.local/bin/capri`, and installs the Super+I menu entry and icon. Hyprland files are not changed unless `--apply-hyprland` is given. The end-of-run diagnostic is read-only and reports missing steps; it never changes the setup script's exit status.

### First launch and Brain setup

1. Close and reopen **Super+I**, search for **Capri** under **Apps**, and launch her.
2. Open the tray/context menu → **Brain settings…**.
3. Create a Gemini API key at [Google AI Studio](https://aistudio.google.com/apikey) in a project with **billing disabled**, then paste it into the Gemini field. Gemini is primary by default. The free tier may use submitted content to improve Google products; avoid sensitive data. Capri cannot verify billing is disabled.
4. Optionally create a free-only OpenRouter key at [OpenRouter API keys](https://openrouter.ai/workspaces/default/keys) and paste it into the OpenRouter backup field. Set a low credit limit on the key. Capri requests only catalog-verified `:free` models and enforces a zero maximum provider price.
5. Choose the provider order/reply language and select **Save and connect**. The keys are saved separately in the user's Capri config with mode `0600`; the installer never reads, prints, copies or migrates them.
6. Send a harmless message such as “hola Capri”. The panel shows provider status and OpenRouter quota information.

No model order or key is copied from another computer. The first provider defaults to Gemini `gemini-3.5-flash-lite`, then `gemini-3.1-flash-lite`, then `gemini-3.8-flash`. A failing Gemini model is cooled down for 1, 5, then 15 minutes. OpenRouter is account-quota limited; changing free model does not bypass an exhausted daily account quota.

### Current Brain behavior and recent fixes

- **Gemini tool calls:** Gemini 3 returns a `thought_signature` with tool calls. Capri preserves it in the assistant message sent with the tool result; dropping it causes HTTP 400 on the follow-up turn.
- **OpenRouter model fallback:** the API accepts at most three model IDs per chat completion. Capri submits the verified free pool in groups of three and tries the next group when a group fails. Every request has a provider `max_price` of zero and paid/unverified responses are rejected.
- **No duplicate side effects:** once a local action (for example opening Spotify) has executed, a later provider failure returns the local action result instead of asking another provider to execute the same action again.
- **Diagnostics:** sanitized provider HTTP errors are written to `~/.config/Capri/brain-errors.log` (latest 50 lines). It is safe to inspect for the provider's error message, but still do not put API keys in logs or screenshots.
- **Reply animation:** text is shown at about 60 characters per second.
- **Quotas:** Gemini's per-project/model free limits can change; check AI Studio usage. OpenRouter's free daily quota is account-wide and resets at 00:00 UTC. Capri reads its available quota endpoint and reports resets; it never switches to a paid OpenRouter model.

### Files and migration

| Path | Contents | Copy to another computer? |
| --- | --- | --- |
| `~/.config/Capri/gemini.key` | This device's Gemini API key, mode 0600 | **No.** Create a key for the destination/project. |
| `~/.config/Capri/openrouter.key` | This device's OpenRouter key, mode 0600 | **No.** Create a key for the destination. |
| `~/.config/Capri/cross.json` | Capri preferences (provider order, model, language, position) | Prefer configure locally; do not copy the whole file with secret-era settings. |
| `~/.config/Capri/openrouter-free.json` | Public OpenRouter model catalog cache, no API key | Optional; Capri refreshes/downloads it itself. |
| `~/.config/Capri/brain-errors.log` | Sanitized recent provider errors | No; local diagnostics only. |
| `$XDG_RUNTIME_DIR/voxtype/state`, `$XDG_RUNTIME_DIR/capri/voice.txt` | Ephemeral voice state/transcript | **Never.** Do not migrate runtime state. |
| `~/.config/voxtype/config.toml` | Machine-specific microphone/model options | Do not overwrite wholesale; setup script backs up and changes only required settings. |
| `~/.config/hypr/` | Personal compositor settings | Do not copy wholesale; add only Capri's documented lines. |

### Hyprland integration

On Hyprland 0.55+ Omarchy's Lua config, Capri's per-window border rule and rescue bind are documented in [HYPRLAND-SETUP.md](HYPRLAND-SETUP.md). The installer applies them only with `--apply-hyprland`, makes backups, and does not add global Voxtype hotkeys. `Super+Shift+C` invokes `capri --rescue`; it requires Capri to be running and `~/.local/bin` on `PATH`.

### Troubleshooting

| Symptom | Likely cause / action |
| --- | --- |
| HTTP 400 mentioning `thought_signature` | Old binary without Gemini signature preservation. Pull latest `linux-features`, republish and restart Capri. |
| HTTP 400 saying `'models' array must have 3 items or fewer` | Old OpenRouter fallback implementation. Pull latest branch and republish. |
| HTTP 429 | Provider/model or account quota. Gemini falls through to another free provider/model; OpenRouter daily quota is account-wide. Check provider status and retry after reset. |
| HTTP 503 | Gemini model/provider temporarily busy. Capri cools down that Gemini model and tries another provider/model. |
| Capri window is off-screen | If Capri is running, press **Super+Shift+C**. Verify class/bind with `hyprctl clients -j` and `hyprctl binds -j`. |
| Wave says Voxtype is busy/unavailable | Check `systemctl --user status voxtype.service`; ensure `$XDG_RUNTIME_DIR/voxtype/state` says `idle`. Wave starts recording only when clicked. |
| Rescue bind cannot find `capri` | Ensure `~/.local/bin` is in Hyprland's PATH and `readlink ~/.local/bin/capri` points to this checkout's `out/Capri/Capri`. |

For manual build/launch, use `./start-capri.sh`; it runs the published executable if present or `dotnet run` otherwise. To publish manually:

```sh
dotnet build cross/Capri.Cross.csproj -c Release
dotnet publish cross/Capri.Cross.csproj -c Release -r linux-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:DebugType=none -o out/Capri
```

Then use `./install-omarchy.sh` to install/update only the menu entry and icon. Do not use `--skip-voxtype --apply-hyprland` as a generic manual publish command; those options configure other system integration.

## How to use her

- **Pencil:** type to her.
- **Wave:** voice chat (local Voxtype, started by clicking Wave).
- **Drag her:** sit her on a window.
- **Right-click her** or the tray icon: menu and settings.

Just ask her for things, like “open Spotify”, “play Blinding Lights”, “pause”, “what's playing?” or “close Discord”.

## Licence

Free for anything non-commercial: use her, share her, change her. No selling her or putting her in paid products. Full terms in [LICENSE.md](LICENSE.md) (PolyForm Noncommercial 1.0.0).
