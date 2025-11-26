// RedactionHelper.cs
using System.Text.RegularExpressions;

public static class RedactionHelper
{
    // Naive: replace each match with stars. You can expand to token-aware redaction.
    public static string Redact(string text, IEnumerable<string> evidence, int maxMaskLen = 8)
    {
        if (string.IsNullOrEmpty(text) || evidence == null) return text;
        string outp = text;
        foreach (var ev in evidence)
        {
            if (string.IsNullOrWhiteSpace(ev)) continue;
            // use regex ignore-case and word-boundary to avoid partial-word clobber
            try
            {
                var safePattern = Regex.Escape(ev);
                var mask = new string('*', Math.Min(ev.Length, maxMaskLen));
                outp = Regex.Replace(outp, safePattern, mask, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
            catch { outp = outp.Replace(ev, new string('*', Math.Min(ev.Length, maxMaskLen))); }
        }
        return outp;
    }
}