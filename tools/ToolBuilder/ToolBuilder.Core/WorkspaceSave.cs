using System.Security.Cryptography;

namespace ToolBuilder.Core;

public sealed record FileWrite(string Path, byte[] Content, byte[]? ExpectedHash);

/// <summary>预检全部目标，先准备临时文件和备份，再逐个替换；失败时回滚本批已写入的文件。</summary>
public static class WorkspaceSave
{
    public static void Write(IReadOnlyList<FileWrite> files)
    {
        var staged = new List<(FileWrite Write, byte[]? Before, string Temp)>();
        var committed = new List<(FileWrite Write, byte[]? Before)>();
        try
        {
            if (files.Select(file => System.IO.Path.GetFullPath(file.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Count)
            {
                throw new IOException("保存目标重复。");
            }
            foreach (var write in files)
            {
                byte[]? before = File.Exists(write.Path) ? File.ReadAllBytes(write.Path) : null;
                Check(write, before);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(write.Path)!);
                string temp = write.Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                staged.Add((write, before, temp));
                File.WriteAllBytes(temp, write.Content);
                if (before is not null)
                {
                    string backup = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(write.Path)!, ConfigDocument.BackupFolder);
                    Directory.CreateDirectory(backup);
                    File.WriteAllBytes(System.IO.Path.Combine(backup, System.IO.Path.GetFileName(write.Path) + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "." + Guid.NewGuid().ToString("N") + ".bak"), before);
                }
            }
            foreach (var item in staged)
            {
                Check(item.Write, File.Exists(item.Write.Path) ? File.ReadAllBytes(item.Write.Path) : null);
                File.Move(item.Temp, item.Write.Path, true);
                committed.Add((item.Write, item.Before));
            }
        }
        catch (Exception saveError)
        {
            var rollbackErrors = new List<string>();
            foreach (var item in committed.AsEnumerable().Reverse())
            {
                try
                {
                    if (!File.ReadAllBytes(item.Write.Path).SequenceEqual(item.Write.Content))
                    {
                        throw new IOException("文件又被外部修改，保留现场文件和备份。");
                    }
                    if (item.Before is null)
                    {
                        File.Delete(item.Write.Path);
                    }
                    else
                    {
                        string restore = item.Write.Path + "." + Guid.NewGuid().ToString("N") + ".restore";
                        File.WriteAllBytes(restore, item.Before);
                        File.Move(restore, item.Write.Path, true);
                    }
                }
                catch (Exception rollbackError)
                {
                    rollbackErrors.Add(item.Write.Path + "：" + rollbackError.Message);
                }
            }
            if (rollbackErrors.Count > 0)
            {
                throw new IOException(saveError.Message + "\n部分文件未能回滚，请检查 backup：\n" + string.Join("\n", rollbackErrors), saveError);
            }
            throw;
        }
        finally
        {
            foreach (var item in staged)
            {
                if (File.Exists(item.Temp))
                {
                    File.Delete(item.Temp);
                }
            }
        }
    }

    private static void Check(FileWrite write, byte[]? current)
    {
        if (write.ExpectedHash is null ? current is not null : current is null || !SHA256.HashData(current).SequenceEqual(write.ExpectedHash))
        {
            throw new IOException($"文件已被外部修改或目标已存在，未覆盖：{write.Path}");
        }
    }
}
