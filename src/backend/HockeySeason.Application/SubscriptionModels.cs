namespace HockeySeason.Application;

/// <summary>Request to create a subscription (team or league, in Spordle ids).</summary>
public sealed record CreateSubscriptionRequest(int EntityId);

/// <summary>Failure reason of a subscription attempt.</summary>
public enum SubscriptionError
{
    None,
    /// <summary>The referenced entity is unknown.</summary>
    UnknownEntity,
    /// <summary>Licence limit reached (e.g. 5 teams per coach).</summary>
    LimitReached,
    /// <summary>The entity is outside the user's authorized scope.</summary>
    OutOfScope,
    /// <summary>The user already follows this entity.</summary>
    Duplicate,
}

/// <summary>Outcome of a subscription attempt.</summary>
public sealed record SubscriptionResult(SubscriptionError Error, DateTimeOffset? LockedUntil);
