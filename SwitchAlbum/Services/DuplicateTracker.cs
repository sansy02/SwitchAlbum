using System.Text.Json;
using SwitchAlbum.Models;

namespace SwitchAlbum.Services;

/// <summary>
/// 重复检测：记录已保存过的项目（saved.json），支持按游戏名+文件名查询与标记。
/// 线程安全；每累计 10 次标记自动落盘。
/// </summary>
public sealed class DuplicateTracker
{
    private readonly string _filePath;
    private readonly Dictionary<string, SavedRecord> _records = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private int _pendingWrites;

    public DuplicateTracker(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SwitchAlbum", "saved.json");
        Load();
    }

    public SavedState GetState(string gameTitle, string fileName)
        => GetStateById(gameTitle + "|" + fileName);

    public SavedState GetStateById(string id)
    {
        lock (_lock)
        {
            if (!_records.TryGetValue(id, out var record))
            {
                return SavedState.NotSaved;
            }

            return (record.SavedToPc, record.SavedToPhone) switch
            {
                (true, true) => SavedState.SavedBoth,
                (true, false) => SavedState.SavedToPc,
                (false, true) => SavedState.SavedToPhone,
                _ => SavedState.NotSaved,
            };
        }
    }

    public void MarkSaved(string gameTitle, string fileName, string? renamedTo, bool toPc, bool toPhone)
    {
        lock (_lock)
        {
            var id = gameTitle + "|" + fileName;
            if (!_records.TryGetValue(id, out var record))
            {
                record = new SavedRecord
                {
                    Id = id,
                    GameTitle = gameTitle,
                    FileName = fileName,
                };
                _records[id] = record;
            }

            record.RenamedTo = renamedTo ?? record.RenamedTo;
            record.SavedToPc |= toPc;
            record.SavedToPhone |= toPhone;
            record.SavedAt = DateTime.Now;

            _pendingWrites++;
        }

        if (_pendingWrites >= 10)
        {
            Flush();
        }
    }

    public void Flush()
    {
        Dictionary<string, SavedRecord> snapshot;
        lock (_lock)
        {
            if (_pendingWrites == 0)
            {
                return;
            }

            _pendingWrites = 0;
            snapshot = new Dictionary<string, SavedRecord>(_records, StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            var tmp = _filePath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _filePath, overwrite: true);
        }
        catch
        {
            // 落盘失败不影响本次保存
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return;
            }

            var dict = JsonSerializer.Deserialize<Dictionary<string, SavedRecord>>(File.ReadAllText(_filePath));
            if (dict == null)
            {
                return;
            }

            lock (_lock)
            {
                foreach (var (key, value) in dict)
                {
                    _records[key] = value;
                }
            }
        }
        catch
        {
            // 记录损坏则从空开始
        }
    }
}
