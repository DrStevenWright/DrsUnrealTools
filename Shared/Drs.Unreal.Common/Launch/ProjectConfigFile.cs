using Drs.Unreal.Common.Ini;

namespace Drs.Unreal.Common.Launch;

/// <summary>
/// The project's shared, committed configuration file (for UE Test Tool: <c>&lt;Project&gt;\Config\UETestTool.ini</c>).
/// <code>
/// [Config.Standalone (noHMD)]
/// Template=Standalone
/// Args=-game -log -noHMD
/// ; Optional rule overrides (normally inherited from Template):
/// ; Map=True|False   MapOptions=?listen   Connect=127.0.0.1
/// </code>
/// <c>Template</c> names a built-in or an earlier-defined configuration. A section named like a built-in
/// (e.g. <c>[Config.Standalone]</c>) overrides that built-in.
/// </summary>
public sealed class ProjectConfigFile
{
    public const string SectionPrefix = "Config.";

    private readonly string _path;
    private IniDocument _doc;

    private ProjectConfigFile(string path, IniDocument doc)
    {
        _path = path;
        _doc = doc;
    }

    public static ProjectConfigFile Load(string path) => new(path, IniDocument.Load(path));

    public string Path => _path;

    /// <summary>Problems found while resolving the file (unknown templates, cycles); shown to the user, never fatal.</summary>
    public List<string> Warnings { get; } = [];

    /// <summary>Built-ins (possibly overridden) followed by the project's own configurations in file order.</summary>
    public IReadOnlyList<LaunchConfig> GetConfigs()
    {
        Warnings.Clear();
        var result = LaunchConfig.BuiltIns.ToList();
        var sections = _doc.SectionNames
            .Where(s => s.StartsWith(SectionPrefix, StringComparison.OrdinalIgnoreCase) && s.Length > SectionPrefix.Length)
            .ToList();

        foreach (var section in sections)
        {
            var name = section[SectionPrefix.Length..].Trim();
            var config = Resolve(name, section, result);
            var existing = result.FindIndex(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0) result[existing] = config;   // override built-in (or an earlier duplicate)
            else result.Add(config);
        }
        return result;
    }

    private LaunchConfig Resolve(string name, string section, IReadOnlyList<LaunchConfig> known)
    {
        var templateName = _doc.Get(section, "Template");
        // A built-in override with no Template inherits from the built-in itself.
        templateName ??= LaunchConfig.FindBuiltIn(name)?.Name;

        LaunchConfig? template = null;
        if (templateName is not null)
        {
            template = string.Equals(templateName, name, StringComparison.OrdinalIgnoreCase)
                ? LaunchConfig.FindBuiltIn(name)
                : known.FirstOrDefault(c => string.Equals(c.Name, templateName, StringComparison.OrdinalIgnoreCase));
            if (template is null)
                Warnings.Add($"[{section}]: Template '{templateName}' is not a built-in or earlier configuration; using Standalone.");
        }
        template ??= LaunchConfig.FindBuiltIn(LaunchConfig.Standalone)!;

        var rule = template.Rule;
        if (_doc.Get(section, "Map") is { } map)
        {
            if (TryParseBool(map, out var usesMap)) rule = rule with { UsesMap = usesMap };
            else Warnings.Add($"[{section}]: Map='{map}' should be True or False.");
        }
        if (_doc.Get(section, "MapOptions") is { } options) rule = rule with { MapOptions = options };
        if (_doc.Get(section, "Connect") is { } connect) rule = rule with { Connect = connect.Length == 0 ? null : connect };

        var args = _doc.Get(section, "Args") ?? template.Args;
        return new LaunchConfig(name, template.Base, rule, args, IsBuiltIn: false);
    }

    /// <summary>
    /// Adds (or replaces) a configuration created in the app from <paramref name="template"/>.
    /// Writes Template as the template's built-in base, plus explicit rule keys only where the template's
    /// rule differs from that base, so the new config never depends on another custom config.
    /// </summary>
    public void Save(string name, LaunchConfig template, string args)
    {
        if (LaunchConfig.ValidateName(name) is { } error) throw new ArgumentException(error, nameof(name));
        var section = SectionPrefix + name;
        var baseConfig = LaunchConfig.FindBuiltIn(template.Base) ?? LaunchConfig.FindBuiltIn(LaunchConfig.Standalone)!;

        if (_doc.SectionNames.Count() == 0)
        {
            _doc.AddHeaderComment(
                "UE Test Tool launch configurations for this project. Commit this file to share them.",
                "Each [Config.<Name>] section: Template=<built-in or earlier config>, Args=<command-line arguments>.",
                "Optional overrides: Map=True|False, MapOptions=<e.g. ?listen>, Connect=<address>.");
        }

        _doc.Set(section, "Template", baseConfig.Name);
        _doc.Set(section, "Args", args.Trim());
        SetOrRemove(section, "Map", template.Rule.UsesMap != baseConfig.Rule.UsesMap ? template.Rule.UsesMap.ToString() : null);
        SetOrRemove(section, "MapOptions", template.Rule.MapOptions != baseConfig.Rule.MapOptions ? template.Rule.MapOptions : null);
        SetOrRemove(section, "Connect", template.Rule.Connect != baseConfig.Rule.Connect ? template.Rule.Connect ?? "" : null);
        _doc.Save(_path);
    }

    /// <summary>Removes a project configuration (or a built-in override). Returns false if the file doesn't define it.</summary>
    public bool Delete(string name)
    {
        if (!_doc.RemoveSection(SectionPrefix + name)) return false;
        _doc.Save(_path);
        return true;
    }

    /// <summary>Whether the file defines this name (i.e. it can be edited/deleted in the app).</summary>
    public bool Defines(string name) => _doc.HasSection(SectionPrefix + name);

    /// <summary>Re-reads the file from disk (e.g. after someone edited it by hand).</summary>
    public void Reload() => _doc = IniDocument.Load(_path);

    private void SetOrRemove(string section, string key, string? value)
    {
        if (value is null) _doc.Remove(section, key);
        else _doc.Set(section, key, value);
    }

    internal static bool TryParseBool(string s, out bool value)
    {
        switch (s.Trim().ToLowerInvariant())
        {
            case "true" or "1" or "yes": value = true; return true;
            case "false" or "0" or "no": value = false; return true;
            default: value = false; return false;
        }
    }
}
