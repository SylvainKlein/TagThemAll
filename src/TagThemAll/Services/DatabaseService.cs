using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;
using TagThemAll.Models;

namespace TagThemAll.Services;

public class DatabaseService : IDisposable
{
    private readonly string _connectionString;
    private readonly SqliteConnection _sharedConnection;
    private readonly object _lock = new();

    public string DatabasePath { get; }

    public DatabaseService(string? customDbPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customDbPath))
        {
            DatabasePath = customDbPath;
        }
        else
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            if (File.Exists(Path.Combine(appDir, "portable.txt")))
            {
                DatabasePath = Path.Combine(appDir, "tagthemall.db");
            }
            else
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string dataFolder = Path.Combine(localAppData, "TagThemAll");
                if (!Directory.Exists(dataFolder))
                {
                    Directory.CreateDirectory(dataFolder);
                }
                DatabasePath = Path.Combine(dataFolder, "tagthemall.db");
            }
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        _sharedConnection = new SqliteConnection(_connectionString);
        _sharedConnection.Open();

        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        lock (_lock)
        {
            using var cmd = _sharedConnection.CreateCommand();
            cmd.CommandText = @"
                PRAGMA journal_mode = WAL;
                PRAGMA synchronous = NORMAL;
                PRAGMA foreign_keys = ON;

                CREATE TABLE IF NOT EXISTS Files (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    AbsolutePath TEXT UNIQUE NOT NULL,
                    FileName TEXT NOT NULL,
                    FileExtension TEXT NOT NULL,
                    FileSize INTEGER NOT NULL,
                    DateModified TEXT,
                    DateAdded TEXT NOT NULL,
                    IsUnlinked INTEGER DEFAULT 0
                );

                CREATE TABLE IF NOT EXISTS Tags (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT UNIQUE NOT NULL COLLATE NOCASE,
                    ColorHex TEXT
                );

                CREATE TABLE IF NOT EXISTS FileTags (
                    FileId INTEGER NOT NULL,
                    TagId INTEGER NOT NULL,
                    PRIMARY KEY (FileId, TagId),
                    FOREIGN KEY (FileId) REFERENCES Files(Id) ON DELETE CASCADE,
                    FOREIGN KEY (TagId) REFERENCES Tags(Id) ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS idx_files_path ON Files(AbsolutePath);
                CREATE INDEX IF NOT EXISTS idx_files_unlinked ON Files(IsUnlinked);
                CREATE INDEX IF NOT EXISTS idx_tags_name ON Tags(Name);
                CREATE INDEX IF NOT EXISTS idx_filetags_file ON FileTags(FileId);
                CREATE INDEX IF NOT EXISTS idx_filetags_tag ON FileTags(TagId);
            ";
            cmd.ExecuteNonQuery();
        }
    }

    public List<FileItem> GetAllFiles()
    {
        lock (_lock)
        {
            var filesMap = new Dictionary<long, FileItem>();

            using (var cmd = _sharedConnection.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT 
                        f.Id, f.AbsolutePath, f.FileName, f.FileExtension, f.FileSize, f.DateModified, f.DateAdded, f.IsUnlinked,
                        t.Id AS TagId, t.Name AS TagName, t.ColorHex AS TagColor
                    FROM Files f
                    LEFT JOIN FileTags ft ON f.Id = ft.FileId
                    LEFT JOIN Tags t ON ft.TagId = t.Id
                    ORDER BY f.Id DESC;
                ";

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    long id = reader.GetInt64(0);
                    if (!filesMap.TryGetValue(id, out var file))
                    {
                        file = new FileItem
                        {
                            Id = id,
                            AbsolutePath = reader.GetString(1),
                            FileName = reader.GetString(2),
                            FileExtension = reader.GetString(3),
                            FileSize = reader.GetInt64(4),
                            DateModified = reader.IsDBNull(5) ? null : DateTime.TryParse(reader.GetString(5), out var dm) ? dm : null,
                            DateAdded = DateTime.TryParse(reader.GetString(6), out var da) ? da : DateTime.UtcNow,
                            IsUnlinked = reader.GetInt32(7) != 0,
                            Tags = new List<TagItem>()
                        };
                        filesMap[id] = file;
                    }

                    if (!reader.IsDBNull(8))
                    {
                        long tagId = reader.GetInt64(8);
                        string tagName = reader.GetString(9);
                        string? tagColor = reader.IsDBNull(10) ? null : reader.GetString(10);
                        file.Tags.Add(new TagItem(tagId, tagName, tagColor));
                    }
                }
            }

            return new List<FileItem>(filesMap.Values);
        }
    }

    public List<TagItem> GetAllTags()
    {
        lock (_lock)
        {
            var list = new List<TagItem>();
            using var cmd = _sharedConnection.CreateCommand();
            cmd.CommandText = "SELECT Id, Name, ColorHex FROM Tags ORDER BY Name COLLATE NOCASE ASC;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new TagItem(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2)
                ));
            }
            return list;
        }
    }

    public List<FileItem> InsertFiles(IEnumerable<string> paths)
    {
        var addedFiles = new List<FileItem>();
        lock (_lock)
        {
            using var transaction = _sharedConnection.BeginTransaction();
            try
            {
                foreach (var rawPath in paths)
                {
                    if (string.IsNullOrWhiteSpace(rawPath)) continue;
                    string fullPath = Path.GetFullPath(rawPath);

                    bool existsOnDisk = File.Exists(fullPath);
                    FileInfo? fi = existsOnDisk ? new FileInfo(fullPath) : null;

                    string fileName = fi != null ? fi.Name : Path.GetFileName(fullPath);
                    string ext = fi != null ? fi.Extension : Path.GetExtension(fullPath);
                    long size = fi != null ? fi.Length : 0;
                    DateTime? dateModified = fi?.LastWriteTimeUtc;
                    DateTime dateAdded = DateTime.UtcNow;

                    using var checkCmd = _sharedConnection.CreateCommand();
                    checkCmd.Transaction = transaction;
                    checkCmd.CommandText = "SELECT Id FROM Files WHERE AbsolutePath = @path;";
                    checkCmd.Parameters.AddWithValue("@path", fullPath);
                    var existingId = checkCmd.ExecuteScalar();
                    if (existingId != null)
                    {
                        continue; // Already exists in catalog
                    }

                    using var insertCmd = _sharedConnection.CreateCommand();
                    insertCmd.Transaction = transaction;
                    insertCmd.CommandText = @"
                        INSERT INTO Files (AbsolutePath, FileName, FileExtension, FileSize, DateModified, DateAdded, IsUnlinked)
                        VALUES (@path, @fileName, @ext, @size, @dateMod, @dateAdd, @unlinked);
                        SELECT last_insert_rowid();
                    ";
                    insertCmd.Parameters.AddWithValue("@path", fullPath);
                    insertCmd.Parameters.AddWithValue("@fileName", fileName);
                    insertCmd.Parameters.AddWithValue("@ext", ext);
                    insertCmd.Parameters.AddWithValue("@size", size);
                    insertCmd.Parameters.AddWithValue("@dateMod", dateModified.HasValue ? (object)dateModified.Value.ToString("o") : DBNull.Value);
                    insertCmd.Parameters.AddWithValue("@dateAdd", dateAdded.ToString("o"));
                    insertCmd.Parameters.AddWithValue("@unlinked", existsOnDisk ? 0 : 1);

                    long newId = (long)insertCmd.ExecuteScalar()!;

                    var item = new FileItem
                    {
                        Id = newId,
                        AbsolutePath = fullPath,
                        FileName = fileName,
                        FileExtension = ext,
                        FileSize = size,
                        DateModified = dateModified,
                        DateAdded = dateAdded,
                        IsUnlinked = !existsOnDisk,
                        Tags = new List<TagItem>()
                    };
                    addedFiles.Add(item);
                }

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        return addedFiles;
    }

    public TagItem GetOrCreateTag(string tagName, string? colorHex = null)
    {
        string cleanName = tagName.Trim();
        lock (_lock)
        {
            using var findCmd = _sharedConnection.CreateCommand();
            findCmd.CommandText = "SELECT Id, Name, ColorHex FROM Tags WHERE Name = @name COLLATE NOCASE;";
            findCmd.Parameters.AddWithValue("@name", cleanName);
            using (var reader = findCmd.ExecuteReader())
            {
                if (reader.Read())
                {
                    return new TagItem(
                        reader.GetInt64(0),
                        reader.GetString(1),
                        reader.IsDBNull(2) ? null : reader.GetString(2)
                    );
                }
            }

            using var insertCmd = _sharedConnection.CreateCommand();
            insertCmd.CommandText = @"
                INSERT INTO Tags (Name, ColorHex) VALUES (@name, @color);
                SELECT last_insert_rowid();
            ";
            insertCmd.Parameters.AddWithValue("@name", cleanName);
            insertCmd.Parameters.AddWithValue("@color", (object?)colorHex ?? DBNull.Value);
            long id = (long)insertCmd.ExecuteScalar()!;

            return new TagItem(id, cleanName, colorHex);
        }
    }

    public void AddTagToFiles(IEnumerable<long> fileIds, TagItem tag)
    {
        lock (_lock)
        {
            using var transaction = _sharedConnection.BeginTransaction();
            try
            {
                foreach (var fileId in fileIds)
                {
                    using var cmd = _sharedConnection.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
                        INSERT OR IGNORE INTO FileTags (FileId, TagId)
                        VALUES (@fileId, @tagId);
                    ";
                    cmd.Parameters.AddWithValue("@fileId", fileId);
                    cmd.Parameters.AddWithValue("@tagId", tag.Id);
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }

    public void RemoveTagFromFile(long fileId, long tagId)
    {
        lock (_lock)
        {
            using var cmd = _sharedConnection.CreateCommand();
            cmd.CommandText = "DELETE FROM FileTags WHERE FileId = @fileId AND TagId = @tagId;";
            cmd.Parameters.AddWithValue("@fileId", fileId);
            cmd.Parameters.AddWithValue("@tagId", tagId);
            cmd.ExecuteNonQuery();
        }
    }

    public void UpdateFileUnlinkedStatus(long fileId, bool isUnlinked)
    {
        lock (_lock)
        {
            using var cmd = _sharedConnection.CreateCommand();
            cmd.CommandText = "UPDATE Files SET IsUnlinked = @unlinked WHERE Id = @id;";
            cmd.Parameters.AddWithValue("@unlinked", isUnlinked ? 1 : 0);
            cmd.Parameters.AddWithValue("@id", fileId);
            cmd.ExecuteNonQuery();
        }
    }

    public void RelinkFile(long fileId, string newAbsolutePath, long newSize, DateTime? newDateModified)
    {
        lock (_lock)
        {
            using var cmd = _sharedConnection.CreateCommand();
            cmd.CommandText = @"
                UPDATE Files 
                SET AbsolutePath = @newPath,
                    FileName = @fileName,
                    FileExtension = @ext,
                    FileSize = @size,
                    DateModified = @dateMod,
                    IsUnlinked = 0
                WHERE Id = @id;
            ";
            cmd.Parameters.AddWithValue("@newPath", newAbsolutePath);
            cmd.Parameters.AddWithValue("@fileName", Path.GetFileName(newAbsolutePath));
            cmd.Parameters.AddWithValue("@ext", Path.GetExtension(newAbsolutePath));
            cmd.Parameters.AddWithValue("@size", newSize);
            cmd.Parameters.AddWithValue("@dateMod", newDateModified.HasValue ? (object)newDateModified.Value.ToString("o") : DBNull.Value);
            cmd.Parameters.AddWithValue("@id", fileId);
            cmd.ExecuteNonQuery();
        }
    }

    public void DeleteFilesFromCatalog(IEnumerable<long> fileIds)
    {
        lock (_lock)
        {
            using var transaction = _sharedConnection.BeginTransaction();
            try
            {
                foreach (var id in fileIds)
                {
                    using var cmd = _sharedConnection.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = "DELETE FROM Files WHERE Id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }

    public void Dispose()
    {
        _sharedConnection?.Dispose();
    }
}
