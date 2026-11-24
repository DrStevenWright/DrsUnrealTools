using System.Text;

namespace Drs.Unreal.Common.Ini;

/// <summary>
/// A minimal UE-style .ini file that round-trips: comments, blank lines, key order, unknown sections and
/// line endings are preserved, and only the lines that are changed get rewritten. Intended for files that
/// people also edit by hand.
/// <para>
/// Format: <c>[Section Name]</c> headers; <c>Key=Value</c> lines (the key ends at the first '=', the value is the
/// rest of the line, trimmed); lines starting with ';' or '#' are comments. Keys are case-insensitive.
/// Duplicate keys: reads return the first; <see cref="Set"/> updates the first.
/// </para>
/// </summary>
public sealed class IniDocument
{
    private readonly List<string> _lines;
    private readonly string _newline;
    private readonly bool _bom;

    private IniDocument(List<string> lines, string newline, bool bom)
    {
        _lines = lines;
        _newline = newline;
        _bom = bom;
    }

    public static IniDocument Empty() => new([], "\r\n", false);

    public static IniDocument Parse(string text, bool bom = false)
    {
        var newline = text.Contains("\r\n") ? "\r\n" : text.Contains('\n') ? "\n" : "\r\n";
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);   // trailing newline
        return new IniDocument(lines, newline, bom);
    }

    /// <summary>Loads a file, or returns an empty document if it doesn't exist.</summary>
    public static IniDocument Load(string path)
    {
        if (!File.Exists(path)) return Empty();
        var bytes = File.ReadAllBytes(path);
        var bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        return Parse(new UTF8Encoding(false).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0)), bom);
    }

    /// <summary>Writes atomically (temp file + replace) so a crash can't leave a half-written file.</summary>
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, ToString(), new UTF8Encoding(_bom));
        File.Move(tmp, path, overwrite: true);
    }

    public override string ToString() =>
        _lines.Count == 0 ? "" : string.Join(_newline, _lines) + _newline;

    // ---- Reading ----------------------------------------------------------------------------------------

    public IEnumerable<string> SectionNames =>
        _lines.Select(ParseHeader).Where(h => h is not null).Select(h => h!);

    public bool HasSection(string section) => FindSection(section) is not null;

    public string? Get(string section, string key)
    {
        if (FindSection(section) is not { } range) return null;
        for (var i = range.Start + 1; i < range.End; i++)
            if (ParseKeyValue(_lines[i]) is { } kv && Eq(kv.Key, key)) return kv.Value;
        return null;
    }

    /// <summary>All key/value pairs of a section, in file order.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> GetAll(string section)
    {
        var result = new List<KeyValuePair<string, string>>();
        if (FindSection(section) is not { } range) return result;
        for (var i = range.Start + 1; i < range.End; i++)
            if (ParseKeyValue(_lines[i]) is { } kv) result.Add(kv);
        return result;
    }

    // ---- Writing ----------------------------------------------------------------------------------------

    /// <summary>Sets a value, adding the key (after the section's last key) or the section (at the end) if needed.</summary>
    public void Set(string section, string key, string value)
    {
        ValidateName(section, nameof(section), "[]");
        ValidateName(key, nameof(key), "=");
        if (value.Contains('\n') || value.Contains('\r')) throw new ArgumentException("Values can't span lines.", nameof(value));

        var line = $"{key}={value}";
        var range = FindSection(section) ?? AppendSection(section);
        var lastKey = range.Start;
        for (var i = range.Start + 1; i < range.End; i++)
        {
            if (ParseKeyValue(_lines[i]) is not { } kv) continue;
            if (Eq(kv.Key, key))
            {
                _lines[i] = line;
                return;
            }
            lastKey = i;
        }
        _lines.Insert(lastKey + 1, line);
    }

    /// <summary>
    /// Adds a key after the section's last key (or adds the section at the end), even if the key is already there.
    /// For keys that may repeat, such as a session's <c>Launch=</c> lines.
    /// </summary>
    public void Add(string section, string key, string value)
    {
        ValidateName(section, nameof(section), "[]");
        ValidateName(key, nameof(key), "=");
        if (value.Contains('\n') || value.Contains('\r')) throw new ArgumentException("Values can't span lines.", nameof(value));

        var range = FindSection(section) ?? AppendSection(section);
        var lastKey = range.Start;
        for (var i = range.Start + 1; i < range.End; i++)
            if (ParseKeyValue(_lines[i]) is not null) lastKey = i;
        _lines.Insert(lastKey + 1, $"{key}={value}");
    }

    /// <summary>Removes every occurrence of a key from a section. Returns true if anything was removed.</summary>
    public bool Remove(string section, string key)
    {
        if (FindSection(section) is not { } range) return false;
        var removed = false;
        for (var i = range.End - 1; i > range.Start; i--)
        {
            if (ParseKeyValue(_lines[i]) is { } kv && Eq(kv.Key, key))
            {
                _lines.RemoveAt(i);
                removed = true;
            }
        }
        return removed;
    }

    /// <summary>
    /// Removes a section with its keys, plus the comment lines directly above its header (they describe it).
    /// Returns true if the section existed.
    /// </summary>
    public bool RemoveSection(string section)
    {
        if (FindSection(section) is not { } range) return false;
        var start = range.Start;
        while (start > 0 && IsComment(_lines[start - 1])) start--;
        var end = range.End;
        // Keep exactly one blank line between the neighbours.
        while (end < _lines.Count && _lines[end].Trim().Length == 0 && start > 0 && _lines[start - 1].Trim().Length == 0) end++;
        _lines.RemoveRange(start, end - start);
        while (_lines.Count > 0 && _lines[^1].Trim().Length == 0) _lines.RemoveAt(_lines.Count - 1);
        return true;
    }

    /// <summary>Prepends comment lines to an empty document (e.g. a header explaining the file).</summary>
    public void AddHeaderComment(params string[] lines)
    {
        _lines.InsertRange(0, lines.Select(l => "; " + l).Append(""));
    }

    // ---- Internals --------------------------------------------------------------------------------------

    /// <summary>[Start, End): Start is the header line, End is the next header (or end of file).</summary>
    private (int Start, int End)? FindSection(string section)
    {
        for (var i = 0; i < _lines.Count; i++)
        {
            if (ParseHeader(_lines[i]) is { } h && Eq(h, section))
            {
                var end = i + 1;
                while (end < _lines.Count && ParseHeader(_lines[end]) is null) end++;
                // Trailing blank/comment lines belong to whatever follows, not to this section.
                while (end - 1 > i && (_lines[end - 1].Trim().Length == 0 || IsComment(_lines[end - 1]))) end--;
                return (i, end);
            }
        }
        return null;
    }

    private (int Start, int End) AppendSection(string section)
    {
        while (_lines.Count > 0 && _lines[^1].Trim().Length == 0) _lines.RemoveAt(_lines.Count - 1);
        if (_lines.Count > 0) _lines.Add("");
        _lines.Add($"[{section}]");
        return (_lines.Count - 1, _lines.Count);
    }

    private static string? ParseHeader(string line)
    {
        var t = line.Trim();
        return t.Length >= 2 && t[0] == '[' && t[^1] == ']' ? t[1..^1].Trim() : null;
    }

    private static KeyValuePair<string, string>? ParseKeyValue(string line)
    {
        var t = line.Trim();
        if (t.Length == 0 || IsComment(t) || t[0] == '[') return null;
        var eq = t.IndexOf('=');
        if (eq <= 0) return null;
        return new(t[..eq].Trim(), t[(eq + 1)..].Trim());
    }

    private static bool IsComment(string line)
    {
        var t = line.TrimStart();
        return t.StartsWith(';') || t.StartsWith('#');
    }

    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static void ValidateName(string name, string param, string forbidden)
    {
        if (string.IsNullOrWhiteSpace(name) || name != name.Trim() || name.IndexOfAny([.. forbidden, '\r', '\n']) >= 0)
            throw new ArgumentException($"Invalid ini {param} name: '{name}'.", param);
    }
}
