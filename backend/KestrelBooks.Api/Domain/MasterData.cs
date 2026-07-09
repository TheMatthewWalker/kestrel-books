namespace KestrelBooks.Api.Domain;

/// <summary>Shared shape for customer and vendor master data.</summary>
public abstract class Contact
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public string Name { get; set; } = "";
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? Postcode { get; set; }
    public string? VatNumber { get; set; }
    public int PaymentTermsDays { get; set; } = 30;
    public string? Notes { get; set; }
    public bool Archived { get; set; }
}

public class Customer : Contact { }
public class Vendor : Contact { }

public enum ItemKind { Product = 0, Service = 1, RawMaterial = 2, FinishedGood = 3 }
public enum VatRate { Standard20 = 0, Reduced5 = 1, Zero = 2, Exempt = 3, OutsideScope = 4 }

public static class VatRates
{
    public static decimal Percent(VatRate r) => r switch
    {
        VatRate.Standard20 => 0.20m,
        VatRate.Reduced5 => 0.05m,
        _ => 0m
    };
}

/// <summary>Product or service list entry. Default accounts drive automatic posting.</summary>
public class Item
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public ItemKind Kind { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal SalesPrice { get; set; }
    public decimal PurchasePrice { get; set; }
    public VatRate DefaultVatRate { get; set; } = VatRate.Standard20;
    public Guid? SalesAccountId { get; set; }     // income account credited on sale
    public Guid? PurchaseAccountId { get; set; }  // expense/COS account debited on purchase

    // ---- Perpetual inventory (optional per item) ----
    /// <summary>When true, purchases go to stock (asset) not expense, sales post COGS,
    /// and quantity/average cost are maintained from movements.</summary>
    public bool TrackStock { get; set; }
    public Guid? InventoryAccountId { get; set; } // stock asset account (RM or FG)
    public Guid? CogsAccountId { get; set; }      // cost of goods sold account
    public decimal QuantityOnHand { get; set; }
    public decimal AvgUnitCost { get; set; }      // weighted average cost (AVCO)

    public bool Archived { get; set; }
}
