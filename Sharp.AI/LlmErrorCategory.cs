namespace Sharp.AI;

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
