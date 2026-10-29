namespace TagThemAll.Models;

public class TagItem
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ColorHex { get; set; }

    public TagItem() { }

    public TagItem(long id, string name, string? colorHex = null)
    {
        Id = id;
        Name = name;
        ColorHex = colorHex;
    }

    public override string ToString() => Name;

    public override bool Equals(object? obj) => obj is TagItem other && Id == other.Id;

    public override int GetHashCode() => Id.GetHashCode();
}
