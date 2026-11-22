using System.Globalization;
using System.Text.RegularExpressions;

namespace Jarvis.Windows.Core;

public enum CommandKind {
    Ai, OpenApp, WebSearch, SpotifySearch, SpotifyPlaylist, Volume,
    Speak, NoteAdd, NoteRead, NoteDelete, FinanceAdd, FinanceReport, FinanceRemote, Timer, MediaToggle,
    MediaNext, MediaPrevious, TypeIntoActiveWindow, OpenSettings
}

public sealed record ParsedCommand(CommandKind Kind, string Argument = "",
    int Value = 0, FinanceDraft? Finance = null);
public sealed record FinanceDraft(bool Income, decimal Amount, string Account,
    string Category, string Note);

public static class CommandRouter {
    private static readonly Regex Prefix = new(
        @"^(?:джарвіс|джарвис|jarvis|дімон|димон|dimon)[\s,:—-]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string StripWake(string text) => Prefix.Replace(text.Trim(), "", 1).Trim();

    public static ParsedCommand Parse(string? raw) {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > 4000)
            return new(CommandKind.Ai, raw?.Trim() ?? "");
        var text = StripWake(raw);
        var s = text.ToLowerInvariant().Trim();
        if (s is "баланс рахунків" or "фінанси" or "фінансовий звіт" or "звіти" or "баланс")
            return new(CommandKind.FinanceReport);
        var expense = Regex.Match(text,
            @"^(витратив|потратив|заробив)\s+([0-9]{1,7}(?:[.,][0-9]{1,2})?)\s+(готівка|карта\s+моно|моно|заначка\s+гривні)\s+([\p{L}ʼ'’-]+)(?:\s+(.{1,100}))?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (expense.Success &&
            decimal.TryParse(expense.Groups[2].Value.Replace(',','.'),
                NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) &&
            amount is > 0 and <= 999999) {
            var income = expense.Groups[1].Value.Equals("заробив", StringComparison.OrdinalIgnoreCase);
            var account = expense.Groups[3].Value.Trim().ToLowerInvariant();
            if (account == "моно") account = "карта моно";
            return new(CommandKind.FinanceAdd, Finance: new(income, amount,
                account, expense.Groups[4].Value.Trim(),
                expense.Groups[5].Value.Trim()));
        }
        Match match;
        match = Regex.Match(text,
            @"^(?:увімкни|умімкни|ввімкни|включи|запусти|постав)\s+(?:пісню|песню|трек|song|track)\s+(.+)$",
            RegexOptions.IgnoreCase);
        if (match.Success) return new(CommandKind.SpotifySearch, match.Groups[1].Value.Trim());
        match = Regex.Match(text,
            @"^(?:увімкни|умімкни|ввімкни|включи|запусти)\s+(?:мій\s+|мою\s+)?(?:плейлист|плейліст|playlist)\s+(.+)$",
            RegexOptions.IgnoreCase);
        if (match.Success) return new(CommandKind.SpotifyPlaylist, match.Groups[1].Value.Trim());
        match = Regex.Match(text, @"^(?:гучність|постав\s+гучність\s+на|встанови\s+гучність\s+на)\s+(\d{1,3})\s*%?$",
            RegexOptions.IgnoreCase);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var volume) &&
            volume is >=0 and <=100) return new(CommandKind.Volume, Value: volume);
        match = Regex.Match(text, @"^(?:таймер|постав\s+таймер\s+на)\s+(\d{1,4})\s*(секунд(?:у|и)?|сек|хвилин(?:у|и)?|хв|minutes?|seconds?)$",
            RegexOptions.IgnoreCase);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var count) &&
            count is >=1 and <=1440) {
            var unit = match.Groups[2].Value.ToLowerInvariant();
            var seconds = unit.StartsWith("сек") || unit.StartsWith("second") ? count : count*60;
            return new(CommandKind.Timer, Value: seconds);
        }
        // Advanced Android JARVIS Finance is authoritative on Pixel.
        // Route the ORIGINAL utterance there; don't rewrite amounts or
        // silently create an independent local Windows transaction.
        if(FinanceCommandClassifier.IsFinance(text))
            return new(CommandKind.FinanceRemote, text);
        match = Regex.Match(text, @"^(?:скажи|промов)\s+(.+)$", RegexOptions.IgnoreCase);
        if (match.Success) return new(CommandKind.Speak, match.Groups[1].Value.Trim());
        // Pixel owns notes, just like the existing Watch7 notes page.
        // Keep the original utterance for the phone's parser.
        if(s is "покажи нотатки" or "мої нотатки" or
           "прочитай нотатки" or "покажи мої нотатки")
            return new(CommandKind.NoteRead);
        if(s is "видали останню нотатку")
            return new(CommandKind.NoteDelete);
        match = Regex.Match(text,
            @"^(?:запиши\s+нотатку|створи\s+нотатку|додай\s+нотатку|нотатка|занотуй)\s+(.{1,180})$",
            RegexOptions.IgnoreCase);
        if (match.Success) return new(CommandKind.NoteAdd, match.Groups[1].Value.Trim());
        match = Regex.Match(text, @"^(?:напиши|введи)\s+(.+)$", RegexOptions.IgnoreCase);
        if (match.Success) return new(CommandKind.TypeIntoActiveWindow, match.Groups[1].Value.Trim());
        if (s is "наступна пісня" or "інша пісня" or "не та пісня" or "наступний трек")
            return new(CommandKind.MediaNext);
        if (s is "попередня пісня" or "попередній трек")
            return new(CommandKind.MediaPrevious);
        if (s is "пауза" or "продовж музику" or "відтворення" or "призупини музику")
            return new(CommandKind.MediaToggle);
        match = Regex.Match(text, @"^(?:відкрий|відкрити|запусти)\s+(.+)$",
            RegexOptions.IgnoreCase);
        if (match.Success) {
            var app = match.Groups[1].Value.Trim();
            if (app is "параметри" or "налаштування windows" or "settings")
                return new(CommandKind.OpenSettings, "ms-settings:");
            return new(CommandKind.OpenApp, app);
        }
        match = Regex.Match(text, @"^(?:знайди|пошукай|пошук\s+в\s+інтернеті)\s+(.+)$",
            RegexOptions.IgnoreCase);
        if (match.Success) return new(CommandKind.WebSearch, match.Groups[1].Value.Trim());
        return new(CommandKind.Ai, text);
    }
}
