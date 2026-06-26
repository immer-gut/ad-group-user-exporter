namespace AdGroupUserExporter;

public sealed class GroupComparisonResult
{
    public static readonly string[] Headers =
    [
        nameof(Status),
        nameof(GroupName),
        nameof(UserA),
        nameof(UserB),
        nameof(GroupPathA),
        nameof(GroupPathB)
    ];

    public string Status { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public string UserA { get; set; } = string.Empty;
    public string UserB { get; set; } = string.Empty;
    public string GroupPathA { get; set; } = string.Empty;
    public string GroupPathB { get; set; } = string.Empty;

    public string[] ToFields()
    {
        return
        [
            Status,
            GroupName,
            UserA,
            UserB,
            GroupPathA,
            GroupPathB
        ];
    }

    public bool Contains(string filter)
    {
        return ToFields().Any(value => value.Contains(filter, StringComparison.CurrentCultureIgnoreCase));
    }
}
