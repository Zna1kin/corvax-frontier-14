ent-ComputerBankATMBase = { "" }
    .desc = { "" }
ent-ComputerBankATMDeposit = банкоман
    .desc = Используется для ввода и вывода средств с личного банковского счета.
ent-ComputerBankATMWithdraw = банкомат (только вывод)
    .desc = Используется для вывода средств с личного банковского счета.
ent-ComputerBankATM = { ent-ComputerBankATMDeposit }
    .desc = { ent-ComputerBankATMDeposit.desc }
ent-ComputerWithdrawBankATM = { ent-ComputerBankATMWithdraw }
    .desc = { ent-ComputerBankATMWithdraw.desc }
ent-ComputerWallmountBankATM = { ent-ComputerBankATMDeposit }
    .suffix = Настенный
    .desc = { ent-ComputerBankATMDeposit.desc }
ent-ComputerWallmountWithdrawBankATM = { ent-ComputerBankATMWithdraw }
    .suffix = Настенный
    .desc = { ent-ComputerBankATMWithdraw.desc }
ent-ComputerBlackMarketBankATM = { ent-ComputerBankATMDeposit }
    .desc = Имеет несколько небрежно выглядящих модификаций и наклейку с надписью "НАЛОГ 30%".
    .suffix = Чёрный Рынок
ent-StationAdminBankATM = консоль стационарного администратора
    .desc = Используется для выплат с банковского счета станции.
