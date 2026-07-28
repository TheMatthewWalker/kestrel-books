namespace KestrelBooks.Api.Domain;

/// <summary>
/// A dimension the business analyses by, alongside the account code — department,
/// site, project, fund, vehicle. The chart of accounts answers "what kind of cost
/// is this?"; tracking answers "whose cost is it?", and having both means the
/// chart does not have to be duplicated per department (4000-Sales-North,
/// 4001-Sales-South and so on), which is how charts of accounts turn unusable.
///
/// Two categories is the practical limit and is what the market settled on:
/// beyond that, reporting becomes unreadable and coding becomes guesswork.
/// </summary>
public class TrackingCategory
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public List<TrackingOption> Options { get; set; } = new();
}

public class TrackingOption
{
    public Guid Id { get; set; }
    public Guid TrackingCategoryId { get; set; }
    public Guid BusinessId { get; set; }
    public string Name { get; set; } = "";
    public bool Archived { get; set; }
}
