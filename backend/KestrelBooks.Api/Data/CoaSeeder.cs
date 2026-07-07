using KestrelBooks.Api.Domain;

namespace KestrelBooks.Api.Data;

/// <summary>
/// Seeds a Sage-style UK default chart of accounts for a new business.
/// Nominal ranges follow the familiar convention:
/// 0xxx fixed assets, 1xxx current assets, 2xxx liabilities,
/// 3xxx capital &amp; reserves, 4xxx sales, 5xxx cost of sales, 6-8xxx overheads.
/// Everything remains editable per business afterwards.
/// </summary>
public static class CoaSeeder
{
    public static List<Account> DefaultChart(Guid businessId)
    {
        var list = new List<Account>();
        void A(string code, string name, AccountType type, string sub,
               bool bank = false, string? tag = null) =>
            list.Add(new Account
            {
                Id = Guid.NewGuid(), BusinessId = businessId, Code = code, Name = name,
                Type = type, SubType = sub, IsBank = bank, SystemTag = tag
            });

        // Fixed assets
        A("0020", "Plant & Machinery — Cost", AccountType.Asset, "Fixed Assets");
        A("0021", "Plant & Machinery — Accumulated Depreciation", AccountType.Asset, "Fixed Assets");
        A("0030", "Office Equipment — Cost", AccountType.Asset, "Fixed Assets");
        A("0031", "Office Equipment — Accumulated Depreciation", AccountType.Asset, "Fixed Assets");
        A("0040", "Motor Vehicles — Cost", AccountType.Asset, "Fixed Assets");
        A("0041", "Motor Vehicles — Accumulated Depreciation", AccountType.Asset, "Fixed Assets");
        A("0050", "Assets Under Construction", AccountType.Asset, "Fixed Assets", tag: SystemTags.AssetsUnderConstruction);

        // Current assets
        A("1001", "Stock", AccountType.Asset, "Current Assets");
        A("1100", "Trade Debtors (Sales Ledger Control)", AccountType.Asset, "Current Assets", tag: SystemTags.TradeDebtors);
        A("1200", "Bank Current Account", AccountType.Asset, "Current Assets", bank: true, tag: SystemTags.DefaultBank);
        A("1210", "Bank Deposit Account", AccountType.Asset, "Current Assets", bank: true);
        A("1230", "Petty Cash", AccountType.Asset, "Current Assets", bank: true);
        A("1250", "VAT on Purchases (Input VAT)", AccountType.Asset, "Current Assets", tag: SystemTags.VatInput);

        // Liabilities
        A("2100", "Trade Creditors (Purchase Ledger Control)", AccountType.Liability, "Current Liabilities", tag: SystemTags.TradeCreditors);
        A("2200", "VAT on Sales (Output VAT)", AccountType.Liability, "Current Liabilities", tag: SystemTags.VatOutput);
        A("2210", "PAYE/NIC Payable", AccountType.Liability, "Current Liabilities");
        A("2300", "Loans", AccountType.Liability, "Long-term Liabilities");
        A("2320", "Corporation Tax Payable", AccountType.Liability, "Current Liabilities");

        // Capital & reserves
        A("3000", "Capital / Share Capital", AccountType.Equity, "Capital & Reserves");
        A("3100", "Drawings / Dividends", AccountType.Equity, "Capital & Reserves");
        A("3200", "Retained Earnings", AccountType.Equity, "Capital & Reserves", tag: SystemTags.RetainedEarnings);

        // Income
        A("4000", "Sales — Goods", AccountType.Income, "Sales");
        A("4001", "Sales — Services", AccountType.Income, "Sales");
        A("4900", "Other Income", AccountType.Income, "Other Income");

        // Cost of sales
        A("5000", "Purchases — Goods for Resale", AccountType.Expense, "Cost of Sales");
        A("5100", "Carriage Inwards", AccountType.Expense, "Cost of Sales");

        // Overheads
        A("7000", "Gross Wages", AccountType.Expense, "Overheads");
        A("7100", "Rent", AccountType.Expense, "Overheads");
        A("7103", "Rates", AccountType.Expense, "Overheads");
        A("7200", "Electricity & Gas", AccountType.Expense, "Overheads");
        A("7300", "Motor Expenses", AccountType.Expense, "Overheads");
        A("7500", "Printing, Postage & Stationery", AccountType.Expense, "Overheads");
        A("7502", "Telephone & Internet", AccountType.Expense, "Overheads");
        A("7600", "Professional Fees", AccountType.Expense, "Overheads");
        A("7900", "Bank Charges & Interest", AccountType.Expense, "Overheads");
        A("8000", "Depreciation — Plant & Machinery", AccountType.Expense, "Overheads");
        A("8001", "Depreciation — Office Equipment", AccountType.Expense, "Overheads");
        A("8002", "Depreciation — Motor Vehicles", AccountType.Expense, "Overheads");
        A("8200", "Sundry Expenses", AccountType.Expense, "Overheads");

        return list;
    }
}
