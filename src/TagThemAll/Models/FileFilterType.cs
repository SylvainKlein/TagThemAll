namespace TagThemAll.Models;

public enum FileFilterType
{
    All,
    Unlinked,
    Untagged,
    Images,
    Videos,
    Audio,
    Documents,
    Other
}

public enum SortField
{
    Name,
    DateModified,
    DateAdded,
    FileSize,
    TagCount
}

public enum SearchTarget
{
    Tags,
    FileName,
    Both
}
