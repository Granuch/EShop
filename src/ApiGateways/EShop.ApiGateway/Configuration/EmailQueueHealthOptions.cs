namespace EShop.ApiGateway.Configuration;

public sealed class EmailQueueHealthOptions
{
    public const string SectionName = "EmailQueueHealth";

    public int BacklogWarningThreshold { get; set; } = 250;
    public int BacklogUnhealthyThreshold { get; set; } = 1000;
    public long DroppedWarningThreshold { get; set; } = 1;
    public long DroppedUnhealthyThreshold { get; set; } = 50;
}
