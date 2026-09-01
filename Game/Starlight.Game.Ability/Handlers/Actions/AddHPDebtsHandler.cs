using Starlight.Game.Ability.HpDebts;

namespace Starlight.Game.Ability.Handlers.Actions;

public sealed class AddHPDebtsHandler(HpDebtService debts) : HpDebtActionHandler(debts, multiplier: +1f);
