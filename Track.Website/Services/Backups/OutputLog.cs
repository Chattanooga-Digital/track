using System.Text;

namespace Track.Website.Services.Backups;

/// <summary>The tail of a job's output, kept small enough to store on the job.</summary>
public sealed class OutputLog(int maxChars = 64 * 1024)
{
    private readonly object _lock = new();
    private readonly LinkedList<string> _lines = new();
    private int _chars;

    public string? LastLine
    {
        get
        {
            lock (_lock)
                return _lines.Last?.Value;
        }
    }

    public void Append(string line)
    {
        lock (_lock)
        {
            _lines.AddLast(line);
            _chars += line.Length + 1;
            while (_chars > maxChars && _lines.First is { } first)
            {
                _chars -= first.Value.Length + 1;
                _lines.RemoveFirst();
            }
        }
    }

    public override string ToString()
    {
        lock (_lock)
            return string.Join('\n', _lines);
    }
}
