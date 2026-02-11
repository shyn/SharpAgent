namespace Sharp.AI.Infrastructure;

public enum LlmErrorCategory
{
    Unknown,
    Aborted,
    Timeout,
    RateLimit,
    Server,
    Network,
    Validation,
    ContextOverflow
}
