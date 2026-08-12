using GrevUltraVNC.Contracts;

namespace GrevUltraVNC.Agent;

public sealed class FileManagementService
{
    private const int MaxTransferChunkBytes = 256 * 1024;

    public async Task<AgentFileResponse> ExecuteAsync(AgentFileRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            return request.Operation.Trim().ToLowerInvariant() switch
            {
                "roots" => ListRoots(),
                "list" => ListDirectory(request.Path),
                "create-folder" => CreateFolder(request.Path, request.Name),
                "rename" => Rename(request.Path, request.Name),
                "delete" => Delete(request.Path),
                "copy" => Copy(request.Path, request.DestinationDirectory),
                "move" => Move(request.Path, request.DestinationDirectory),
                "read-chunk" => await ReadChunkAsync(request.Path, request.Offset, cancellationToken),
                "write-chunk" => await WriteChunkAsync(request.Path, request.Offset, request.DataBase64, request.Truncate, cancellationToken),
                _ => Fail($"Unsupported file operation '{request.Operation}'.")
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Fail(ex.Message);
        }
    }

    private static AgentFileResponse ListRoots()
    {
        var entries = DriveInfo.GetDrives()
            .Where(drive => drive.IsReady)
            .OrderBy(drive => drive.Name, StringComparer.OrdinalIgnoreCase)
            .Select(drive => new AgentFileEntry(
                drive.Name,
                drive.RootDirectory.FullName,
                true,
                0,
                null,
                BuildDriveDetail(drive)))
            .ToArray();

        return new AgentFileResponse(true, $"{entries.Length} drive{(entries.Length == 1 ? string.Empty : "s")}", Entries: entries);
    }

    private static AgentFileResponse ListDirectory(string? path)
    {
        var fullPath = NormalizePath(path);
        if (!Directory.Exists(fullPath))
            return Fail("Remote folder does not exist.");

        var directory = new DirectoryInfo(fullPath);
        var entries = new List<AgentFileEntry>();

        foreach (var child in directory.EnumerateDirectories().OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                entries.Add(new AgentFileEntry(
                    child.Name,
                    child.FullName,
                    true,
                    0,
                    new DateTimeOffset(child.LastWriteTimeUtc),
                    "Folder"));
            }
            catch
            {
                // Skip a child that disappears or becomes inaccessible while enumerating.
            }
        }

        foreach (var child in directory.EnumerateFiles().OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                entries.Add(new AgentFileEntry(
                    child.Name,
                    child.FullName,
                    false,
                    child.Length,
                    new DateTimeOffset(child.LastWriteTimeUtc),
                    child.Extension));
            }
            catch
            {
                // Skip a child that disappears or becomes inaccessible while enumerating.
            }
        }

        return new AgentFileResponse(true, $"{entries.Count} item{(entries.Count == 1 ? string.Empty : "s")}", fullPath, entries);
    }

    private static AgentFileResponse CreateFolder(string? parentPath, string? name)
    {
        var parent = NormalizePath(parentPath);
        EnsureDirectoryExists(parent);
        var safeName = NormalizeLeafName(name);
        var target = Path.Combine(parent, safeName);
        Directory.CreateDirectory(target);
        return new AgentFileResponse(true, $"Created folder '{safeName}'.", parent);
    }

    private static AgentFileResponse Rename(string? path, string? newName)
    {
        var source = NormalizePath(path);
        EnsureNotDriveRoot(source, "Drive roots cannot be renamed.");
        var safeName = NormalizeLeafName(newName);
        var parent = Path.GetDirectoryName(source) ?? throw new IOException("Could not determine the parent folder.");
        var destination = Path.Combine(parent, safeName);

        if (File.Exists(destination) || Directory.Exists(destination))
            return Fail("An item with that name already exists.");

        if (Directory.Exists(source))
            Directory.Move(source, destination);
        else if (File.Exists(source))
            File.Move(source, destination);
        else
            return Fail("Remote item does not exist.");

        return new AgentFileResponse(true, $"Renamed to '{safeName}'.", parent);
    }

    private static AgentFileResponse Delete(string? path)
    {
        var target = NormalizePath(path);
        EnsureNotDriveRoot(target, "Drive roots cannot be deleted.");

        if (Directory.Exists(target))
        {
            Directory.Delete(target, recursive: true);
            return new AgentFileResponse(true, "Folder deleted.");
        }

        if (File.Exists(target))
        {
            File.Delete(target);
            return new AgentFileResponse(true, "File deleted.");
        }

        return Fail("Remote item does not exist.");
    }

    private static AgentFileResponse Copy(string? sourcePath, string? destinationDirectory)
    {
        var source = NormalizePath(sourcePath);
        var destinationFolder = NormalizePath(destinationDirectory);
        EnsureDirectoryExists(destinationFolder);
        var destination = Path.Combine(destinationFolder, Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar)));

        if (File.Exists(destination) || Directory.Exists(destination))
            return Fail("An item with the same name already exists in the destination.");

        if (Directory.Exists(source))
        {
            EnsureDestinationOutsideSource(source, destination);
            CopyDirectory(source, destination);
            return new AgentFileResponse(true, "Folder copied.", destinationFolder);
        }

        if (File.Exists(source))
        {
            File.Copy(source, destination, overwrite: false);
            return new AgentFileResponse(true, "File copied.", destinationFolder);
        }

        return Fail("Remote item does not exist.");
    }

    private static AgentFileResponse Move(string? sourcePath, string? destinationDirectory)
    {
        var source = NormalizePath(sourcePath);
        EnsureNotDriveRoot(source, "Drive roots cannot be moved.");
        var destinationFolder = NormalizePath(destinationDirectory);
        EnsureDirectoryExists(destinationFolder);
        var destination = Path.Combine(destinationFolder, Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar)));

        if (File.Exists(destination) || Directory.Exists(destination))
            return Fail("An item with the same name already exists in the destination.");

        if (Directory.Exists(source))
        {
            EnsureDestinationOutsideSource(source, destination);
            Directory.Move(source, destination);
            return new AgentFileResponse(true, "Folder moved.", destinationFolder);
        }

        if (File.Exists(source))
        {
            File.Move(source, destination);
            return new AgentFileResponse(true, "File moved.", destinationFolder);
        }

        return Fail("Remote item does not exist.");
    }

    private static async Task<AgentFileResponse> ReadChunkAsync(string? path, long offset, CancellationToken cancellationToken)
    {
        var target = NormalizePath(path);
        if (!File.Exists(target))
            return Fail("Remote file does not exist.");
        if (offset < 0)
            return Fail("Invalid file offset.");

        await using var stream = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, MaxTransferChunkBytes, useAsync: true);
        if (offset > stream.Length)
            return Fail("File offset is beyond the end of the remote file.");

        stream.Position = offset;
        var remaining = stream.Length - offset;
        var bytesToRead = (int)Math.Min(MaxTransferChunkBytes, remaining);
        var buffer = new byte[bytesToRead];
        var read = bytesToRead == 0 ? 0 : await stream.ReadAsync(buffer.AsMemory(0, bytesToRead), cancellationToken);
        var nextOffset = offset + read;
        var complete = nextOffset >= stream.Length;

        return new AgentFileResponse(
            true,
            complete ? "Download complete." : "File chunk read.",
            DataBase64: Convert.ToBase64String(buffer, 0, read),
            NextOffset: nextOffset,
            Complete: complete);
    }

    private static async Task<AgentFileResponse> WriteChunkAsync(
        string? path,
        long offset,
        string? dataBase64,
        bool truncate,
        CancellationToken cancellationToken)
    {
        var target = NormalizePath(path);
        EnsureNotDriveRoot(target, "Cannot write file data to a drive root.");
        if (offset < 0)
            return Fail("Invalid file offset.");

        byte[] data;
        try
        {
            data = Convert.FromBase64String(dataBase64 ?? string.Empty);
        }
        catch (FormatException)
        {
            return Fail("File transfer chunk was malformed.");
        }

        if (data.Length > MaxTransferChunkBytes)
            return Fail($"File transfer chunks are limited to {MaxTransferChunkBytes / 1024} KB.");

        var parent = Path.GetDirectoryName(target) ?? throw new IOException("Could not determine the target folder.");
        EnsureDirectoryExists(parent);

        var mode = truncate && offset == 0 ? FileMode.Create : FileMode.OpenOrCreate;
        await using var stream = new FileStream(target, mode, FileAccess.Write, FileShare.None, MaxTransferChunkBytes, useAsync: true);
        stream.Position = offset;
        if (data.Length > 0)
            await stream.WriteAsync(data.AsMemory(), cancellationToken);
        await stream.FlushAsync(cancellationToken);

        return new AgentFileResponse(true, "File chunk written.", NextOffset: offset + data.Length, Complete: true);
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A fully-qualified remote path is required.");

        var candidate = path.Trim();
        if (candidate.StartsWith("\\\\", StringComparison.Ordinal) ||
            candidate.StartsWith("\\\\?\\", StringComparison.Ordinal) ||
            candidate.StartsWith("\\\\.\\", StringComparison.Ordinal))
            throw new ArgumentException("UNC and Windows device paths are not supported by Grev Agent file management.");

        if (!Path.IsPathFullyQualified(candidate))
            throw new ArgumentException("A fully-qualified remote path is required.");

        return Path.GetFullPath(candidate);
    }

    private static string NormalizeLeafName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A name is required.");

        var value = name.Trim();
        if (value is "." or ".." || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || value.Contains(Path.DirectorySeparatorChar) || value.Contains(Path.AltDirectorySeparatorChar))
            throw new ArgumentException("The name contains invalid characters.");

        return value;
    }

    private static void EnsureDirectoryExists(string path)
    {
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException("Remote folder does not exist.");
    }

    private static void EnsureNotDriveRoot(string path, string message)
    {
        var root = Path.GetPathRoot(path);
        if (!string.IsNullOrWhiteSpace(root) && string.Equals(path.TrimEnd(Path.DirectorySeparatorChar), root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new IOException(message);
    }

    private static void EnsureDestinationOutsideSource(string source, string destination)
    {
        var sourceWithSlash = source.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var destinationWithSlash = destination.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (destinationWithSlash.StartsWith(sourceWithSlash, StringComparison.OrdinalIgnoreCase))
            throw new IOException("A folder cannot be copied or moved inside itself.");
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: false);
        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private static string BuildDriveDetail(DriveInfo drive)
    {
        var label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? drive.DriveType.ToString() : drive.VolumeLabel;
        return $"{label} · {FormatBytes(drive.AvailableFreeSpace)} free of {FormatBytes(drive.TotalSize)}";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.#} {units[unit]}";
    }

    private static AgentFileResponse Fail(string message) => new(false, message);
}
