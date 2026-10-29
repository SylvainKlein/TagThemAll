using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TagThemAll.Models;

namespace TagThemAll.Services;

public class RelinkResult
{
    public int TotalTargeted { get; set; }
    public int SuccessfullyRelinked { get; set; }
    public int StillUnlinked { get; set; }
    public List<(FileItem file, string newPath)> RelinkedPairs { get; set; } = new();
}

public class RelinkService
{
    private readonly DatabaseService _databaseService;

    public RelinkService(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    public async Task<RelinkResult> RelinkAsync(
        IEnumerable<FileItem> unlinkedFiles,
        string searchRootFolder,
        IProgress<(int current, int total, string message)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new RelinkResult();
        var targetList = unlinkedFiles.Where(f => f.IsUnlinked || !File.Exists(f.AbsolutePath)).ToList();
        result.TotalTargeted = targetList.Count;

        if (targetList.Count == 0 || !Directory.Exists(searchRootFolder))
        {
            return result;
        }

        progress?.Report((0, targetList.Count, "Scanning folder..."));

        // 1. Scan directory recursively in background
        var diskFilesLookup = await Task.Run(() =>
        {
            var lookup = new Dictionary<string, List<FileInfo>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var dirInfo = new DirectoryInfo(searchRootFolder);
                // Safe enumeration skipping inaccessible directories
                var enumerationOptions = new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = true,
                    AttributesToSkip = FileAttributes.ReparsePoint
                };

                foreach (var file in dirInfo.EnumerateFiles("*", enumerationOptions))
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    if (!lookup.TryGetValue(file.Name, out var list))
                    {
                        list = new List<FileInfo>();
                        lookup[file.Name] = list;
                    }
                    list.Add(file);
                }
            }
            catch
            {
                // Directory enumeration error handling
            }
            return lookup;
        }, cancellationToken);

        if (cancellationToken.IsCancellationRequested) return result;

        // 2. Match unlinked files against found files
        int processed = 0;
        foreach (var fileItem in targetList)
        {
            if (cancellationToken.IsCancellationRequested) break;
            processed++;

            progress?.Report((processed, targetList.Count, $"Matching {fileItem.FileName}..."));

            if (diskFilesLookup.TryGetValue(fileItem.FileName, out var candidates) && candidates.Count > 0)
            {
                FileInfo bestMatch = candidates[0];

                if (candidates.Count > 1)
                {
                    // If multiple matches, find one with matching size
                    var sizeMatch = candidates.FirstOrDefault(c => c.Length == fileItem.FileSize);
                    if (sizeMatch != null)
                    {
                        bestMatch = sizeMatch;
                    }
                }

                // Relink
                string newPath = bestMatch.FullName;
                long newSize = bestMatch.Length;
                DateTime newMod = bestMatch.LastWriteTimeUtc;

                _databaseService.RelinkFile(fileItem.Id, newPath, newSize, newMod);
                ShellThumbnailService.Instance.InvalidateCache(fileItem.AbsolutePath);

                fileItem.AbsolutePath = newPath;
                fileItem.FileSize = newSize;
                fileItem.DateModified = newMod;
                fileItem.IsUnlinked = false;

                result.SuccessfullyRelinked++;
                result.RelinkedPairs.Add((fileItem, newPath));
            }
            else
            {
                result.StillUnlinked++;
            }
        }

        return result;
    }
}
