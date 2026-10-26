using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    internal static class MediaArbiter
    {
        private static string pending;
        private static List<string> choices;

        internal static string ActivePlayer()
        {
            List<string> playing = LinuxHands.PlayingPlayers();
            return playing.Count == 1 ? playing[0] : playing.Count > 1 ? "ambiguous" : null;
        }

        internal static bool TryResolve(string text, Talk talk)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (choices != null)
            {
                string chosen = Choose(text, choices);
                if (chosen == null) return false;
                if (chosen == "all" || chosen == "none")
                {
                    foreach (string player in choices) if (chosen == "none") LinuxHands.Pause(player);
                }
                else
                {
                    foreach (string player in choices) if (player != chosen) LinuxHands.Pause(player);
                    ApplyRequestedAction(chosen, pending);
                }
                choices = null;
                pending = null;
                talk.Answer(text, chosen == "none" ? "Okay, they're both paused." : chosen == "all" ? "Okay, I'll keep both playing." : "Okay, keeping " + LinuxHands.PlayerName(chosen) + " playing.");
                return true;
            }

            List<string> playing = LinuxHands.PlayingPlayers();
            if (playing.Count == 0) return false;
            if (playing.Count == 1)
            {
                if (choices != null)
                {
                    string chosen = Choose(text, choices);
                    if (chosen == null) return false;
                    choices = null;
                    pending = null;
                    talk.Answer(text, "Okay, keeping " + LinuxHands.PlayerName(playing[0]) + " playing.");
                    return true;
                }
                return false;
            }
            bool wantsSpotify = Regex.IsMatch(text, @"\b(spotify|quiero escuchar spotify|solo spotify|pon(ga)? spotify|play spotify)\b", RegexOptions.IgnoreCase);
            bool wantsBrowser = Regex.IsMatch(text, @"\b(chrome|google chrome|youtube|quiero escuchar youtube|solo youtube|pon(ga)? youtube)\b", RegexOptions.IgnoreCase);
            bool intent = Regex.IsMatch(text, @"\b(quiero escuchar|solo|pon|poner|play|switch to|use|usa|escuchar)\b", RegexOptions.IgnoreCase);
            if ((wantsSpotify || wantsBrowser) && intent)
            {
                string keep = null;
                foreach (string player in playing)
                {
                    string name = LinuxHands.PlayerName(player);
                    if ((wantsSpotify && name == "Spotify") || (wantsBrowser && (name.Contains("Chrome") || name.Contains("YouTube")))) keep = player;
                }
                if (keep == null) return false;
                foreach (string player in playing) if (player != keep) LinuxHands.Pause(player);
                choices = new List<string> { keep };
                pending = "";
                talk.Answer(text, "Okay, I'll keep " + LinuxHands.PlayerName(keep) + " playing.");
                return true;
            }
            if (!Regex.IsMatch(text, @"\b(pausa|pausar|pause|play.?pause|siguiente|next|anterior|previous|qué suena|que suena|what('?s| is) playing)\b", RegexOptions.IgnoreCase)) return false;
            choices = new List<string>(playing);
            pending = text;
            List<string> names = new List<string>();
            foreach (string player in playing)
            {
                string name = LinuxHands.PlayerName(player);
                string track = LinuxHands.PlayerTrack(player);
                names.Add(name + (string.IsNullOrEmpty(track) ? "" : " (" + track + ")"));
            }
            talk.Answer(text, string.Join(" and ", names.ToArray()) + " are playing. Which one should I keep? Say a player name, 'both', or 'neither'.");
            return true;
        }

        private static void ApplyRequestedAction(string player, string request)
        {
            if (Regex.IsMatch(request ?? "", @"\b(pausa|pausar|pause|play.?pause)\b", RegexOptions.IgnoreCase)) LinuxHands.Pause(player);
            else if (Regex.IsMatch(request ?? "", @"\b(siguiente|next)\b", RegexOptions.IgnoreCase)) LinuxHands.Next(player);
            else if (Regex.IsMatch(request ?? "", @"\b(anterior|previous)\b", RegexOptions.IgnoreCase)) LinuxHands.Previous(player);
        }

        internal static string Choose(string text, IList<string> players)
        {
            string lower = (text ?? "").Trim().ToLowerInvariant();
            if (Regex.IsMatch(lower, @"\b(none|neither|ninguno|ninguna|pausa ambos|pause both|stop both)\b")) return "none";
            if (Regex.IsMatch(lower, @"\b(both|ambos|las dos|los dos|keep both|deja los dos)\b")) return "all";
            foreach (string player in players)
            {
                string name = LinuxHands.PlayerName(player).ToLowerInvariant();
                if (name.Length > 0 && lower.Contains(name)) return player;
                if ((name == "chrome" || name == "google chrome") && Regex.IsMatch(lower, @"\b(chrome|google chrome|youtube|navegador)\b")) return player;
                if (name == "spotify" && Regex.IsMatch(lower, @"\bspotify\b")) return player;
            }
            return null;
        }

        internal static string[] PlayerNamesForTest(IList<string> players)
        {
            List<string> names = new List<string>();
            foreach (string player in players) names.Add(LinuxHands.PlayerName(player));
            return names.ToArray();
        }

        internal static void ResetForTest() { pending = null; choices = null; }

        internal static bool ShouldPause(List<string> playing, string requested)
        {
            if (playing == null || playing.Count < 2) return false;
            bool hasExplicitTarget = Regex.IsMatch(requested ?? "", @"\b(quiero escuchar|solo|pon(ga)?|poner|play|switch to|use|usa|escuchar)\s+\w+", RegexOptions.IgnoreCase);
            return !hasExplicitTarget;
        }
    }
}
