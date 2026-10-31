namespace TaskNova.Core.Search;

/// <summary>
/// Search box logic: every word must appear in one of the fields (case-insensitive). A number also matches a PID exactly,
/// "pid:1234" and "session:2" filter precisely.
/// </summary>
public sealed class SearchMatcher
{
    private readonly string[] _words;
    private readonly int? _pid;
    private readonly int? _session;

    public SearchMatcher(string? text)
    {
        Text = text?.Trim() ?? "";
        var words = new List<string>();
        foreach (var part in Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.StartsWith("pid:", StringComparison.OrdinalIgnoreCase) && int.TryParse(part[4..], out var pid))
            {
                _pid = pid;
            }
            else if (part.StartsWith("session:", StringComparison.OrdinalIgnoreCase) && int.TryParse(part[8..], out var session))
            {
                _session = session;
            }
            else
            {
                words.Add(part);
            }
        }

        _words = words.ToArray();
    }

    public string Text { get; }

    public bool IsEmpty => _words.Length == 0 && _pid is null && _session is null;

    public bool Matches(int pid, int session, params string?[] fields)
    {
        if (_pid is { } p && p != pid)
        {
            return false;
        }

        if (_session is { } s && s != session)
        {
            return false;
        }

        foreach (var word in _words)
        {
            var found = int.TryParse(word, out var number) && number == pid;
            if (!found)
            {
                foreach (var field in fields)
                {
                    if (field is not null && field.Contains(word, StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        break;
                    }
                }
            }

            if (!found)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>For lists without process id.</summary>
    public bool Matches(params string?[] fields) => _pid is null && _session is null && Matches(-1, -1, fields);
}
