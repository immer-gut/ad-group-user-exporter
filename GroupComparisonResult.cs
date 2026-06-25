namespace AdGroupUserExporter;

public sealed class GroupComparisonResult
{
    public static readonly string[] Headers =
    [
        nameof(Status),
        nameof(GroupName),
        nameof(UserA),
        nameof(UserB),
        nameof(DistinguishedName)
    ];

    public string Status { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public string UserA { get; set; } = string.Empty;
    public string UserB { get; set; } = string.Empty;
    public string DistinguishedName { get; set; } = string.Empty;

    public string[] ToFields()
    {
        return
        [
            Status,
            GroupName,
            UserA,
            UserB,
            DistinguishedName
        ];
    }

    public bool Contains(string filter)
    {
        return ToFields().Any(value => value.Contains(filter, StringComparison.CurrentCultureIgnoreCase));
    }
}
