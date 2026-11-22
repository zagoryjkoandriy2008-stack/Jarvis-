using System.Globalization;
using System.Text;

namespace Jarvis.Windows.Core;

public sealed class FinanceService {
    private readonly JarvisState _state;
    private readonly StateStore _store;
    public FinanceService(JarvisState state,StateStore store) {
        _state=state;_store=store;
    }
    public string Add(FinanceDraft draft) {
        if(draft.Amount<=0 || draft.Amount>999999)
            throw new InvalidOperationException("Сума має бути більшою за нуль.");
        var entry=new MoneyEntry {
            Income=draft.Income, Amount=draft.Amount, Account=draft.Account,
            Category=draft.Category, Note=draft.Note
        };
        _state.Money.Add(entry);
        _store.Save(_state);
        return (entry.Income?"Дохід":"Витрата")+" "+
            entry.Amount.ToString("0.##",CultureInfo.InvariantCulture)+
            " ₴ · "+entry.Account+" · "+entry.Category;
    }
    public string Report() {
        var now=DateTimeOffset.Now;
        var thisMonth=_state.Money.Where(x=>x.CreatedAt.Year==now.Year &&
            x.CreatedAt.Month==now.Month).ToArray();
        var expense=thisMonth.Where(x=>!x.Income).Sum(x=>x.Amount);
        var income=thisMonth.Where(x=>x.Income).Sum(x=>x.Amount);
        var result=new StringBuilder();
        result.AppendLine("JARVIS FINANCE / WINDOWS — окрема локальна база");
        result.AppendLine($"Період: {now:yyyy-MM}");
        result.AppendLine($"Доходи: {income:N2} ₴");
        result.AppendLine($"Витрати: {expense:N2} ₴");
        result.AppendLine($"Різниця: {income-expense:N2} ₴");
        result.AppendLine();
        result.AppendLine("За категоріями:");
        foreach(var group in thisMonth.Where(x=>!x.Income)
            .GroupBy(x=>x.Category).OrderByDescending(x=>x.Sum(e=>e.Amount)))
            result.AppendLine($"• {group.Key}: {group.Sum(x=>x.Amount):N2} ₴");
        result.AppendLine();
        result.AppendLine("Останні транзакції:");
        foreach(var e in _state.Money.OrderByDescending(x=>x.CreatedAt).Take(20))
            result.AppendLine($"{e.CreatedAt:dd.MM HH:mm}  {(e.Income?"+":"-")}{e.Amount:0.##} ₴  {e.Account} · {e.Category} {e.Note}");
        return result.ToString();
    }
}
