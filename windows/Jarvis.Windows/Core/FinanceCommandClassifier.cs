using System.Text.RegularExpressions;
namespace Jarvis.Windows.Core;

/** Client-side explicit FINANCE routing ONLY, never parses/calculates money.
 * Pixel is the authority and validates the original recognized command via
 * FinanceVoiceCommands / FinanceInsightsCommands / FinanceVoiceQueries. */
public static class FinanceCommandClassifier {
    private static readonly Regex FinanceQuestion=new(
        @"^(?:(?:скільки|покажи|який|яка|мої|мій|що з|розкажи|скажи|які|назви)\s+).*(?:витрат|витратив|зароб|дохід|доход|залиш|баланс|бюджет|капітал|подушк|чайов|чаєв|фінанс|крипто|платеж|платіж|собіварт|курс|кошт|грошей).*$",
        RegexOptions.IgnoreCase|RegexOptions.CultureInvariant|RegexOptions.Compiled);
    private static readonly Regex FinanceChange=new(
        @"^(?:(?:встанови|задай|зміни|онови|постав)\s+(?:місячний\s+)?бюджет\b|(?:запиши|додай|занеси|зафіксуй)\s+(?:витрату|витрати|дохід|надходження)\b|(?:не\s+\d{1,7}(?:[,.]\d{1,2})?\s*(?:грн)?[,;]?\s+а\s+\d{1,7}(?:[,.]\d{1,2})?\b)).*$",
        RegexOptions.IgnoreCase|RegexOptions.CultureInvariant|RegexOptions.Compiled);
    public static bool IsFinance(string? raw) {
        if(string.IsNullOrWhiteSpace(raw) || raw.Length>250)return false;
        var s=CommandRouter.StripWake(raw).Trim().ToLowerInvariant();
        if(s is "звіти" or "фінансові звіти" or "фінансовий звіт" or
           "звіт фінанси" or "баланси рахунків" or "баланс рахунків" or
           "залишок бюджету" or "що з моїми фінансами" or
           "покажи фінанси" or "відкрий фінанси")return true;
        return FinanceQuestion.IsMatch(s) || FinanceChange.IsMatch(s);
    }
}
