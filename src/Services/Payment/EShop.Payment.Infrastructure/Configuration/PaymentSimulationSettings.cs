namespace EShop.Payment.Infrastructure.Configuration;

public enum PaymentSimulationMode
{
    Random,
    AlwaysSuccess,
    AlwaysFailure
}

public sealed class PaymentSimulationSettings
{
    public const string SectionName = "PaymentSimulation";

    public int ProcessingDelayMinSeconds { get; init; } = 1;
    public int ProcessingDelayMaxSeconds { get; init; } = 3;
    public int SuccessRatePercent { get; init; } = 80;
    public int RefundDelaySeconds { get; init; } = 2;
    public PaymentSimulationMode Mode { get; init; } = PaymentSimulationMode.Random;
    public int? RandomSeed { get; init; }
    public string ForcedFailureReason { get; init; } = "Forced failure by simulation mode.";
}
