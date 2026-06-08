namespace AdGroupUserExporter;

public sealed class AdUserResult
{
    public static readonly string[] Headers =
    [
        nameof(GroupName),
        nameof(GroupPath),
        nameof(SamAccountName),
        nameof(DisplayName),
        nameof(Mail),
        nameof(Enabled),
        nameof(Department),
        nameof(Title),
        nameof(DistinguishedName)
    ];

    public string GroupName { get; set; } = string.Empty;
    public string GroupPath { get; set; } = string.Empty;
    public string SamAccountName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Mail { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string Department { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string DistinguishedName { get; set; } = string.Empty;

    public string[] ToFields()
    {
        return
        [
            GroupName,
            GroupPath,
            SamAccountName,
            DisplayName,
            Mail,
            Enabled.ToString(),
            Department,
            Title,
            DistinguishedName
        ];
    }

    public bool Contains(string filter)
    {
        return ToFields().Any(value => value.Contains(filter, StringComparison.CurrentCultureIgnoreCase));
    }
}
