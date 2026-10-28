namespace Hearthtalk
{
    /// <summary>Tiny UI translation helper: English by default, Spanish when chosen in Settings.</summary>
    internal static class T
    {
        internal static bool Spanish
        {
            get
            {
                try { return (Cfg.InterfaceLanguage.Value ?? "").StartsWith("Span", System.StringComparison.OrdinalIgnoreCase); }
                catch { return false; }
            }
        }

        internal static string Get(string es, string en) => Spanish ? es : en;

        /// <summary>Language the colonists should speak, in English (the prompts are written in English).</summary>
        internal static string StoryLanguage
        {
            get
            {
                string l = Cfg.Language.Value;
                if (!string.IsNullOrWhiteSpace(l) && l != "auto") return l;
                try
                {
                    string cur = I2.Loc.LocalizationManager.CurrentLanguage;
                    return string.IsNullOrEmpty(cur) ? "English" : cur;
                }
                catch { return "English"; }
            }
        }
    }
}
