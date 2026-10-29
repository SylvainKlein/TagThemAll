using System;
using System.Collections.Generic;

namespace TagThemAll.Models;

public class FileItem
{
    public long Id { get; set; }
    public string AbsolutePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FileExtension { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DateTime? DateModified { get; set; }
    public DateTime DateAdded { get; set; } = DateTime.UtcNow;
    public bool IsUnlinked { get; set; }

    public List<TagItem> Tags { get; set; } = new();

    public override string ToString() => FileName;
}
