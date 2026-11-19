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
    /// <summary>
    /// The user already follows this entity.
    /// </summary>
    Duplicate,
    /// <summary>The 15-day removal lock of a team subscription is still active.</summary>
    Locked,
}

/// <summary>Outcome of a subscription attempt.</summary>
/// <param name="Error">The failure reason, <see cref="SubscriptionError.None"/> on success.</param>
/// <param name="LockedUntil">
/// For a created team follow: the end of the 15-day removal lock.
/// </param>
/// <param name="Limit">
/// Licence headroom after the attempt, so the caller can show "3 / 5". Null
/// when the entity kind has no licence limit (organization, open access).
/// </param>
public sealed record SubscriptionResult(
    SubscriptionError Error,
    DateTimeOffset? LockedUntil,
    SubscriptionLimit? Limit = null);

/// <summary>
/// Licence headroom of one follow kind (team, league or organization), built
/// from the caller's role limits: <see cref="Max"/> is the largest
/// <c>roles.max_followed_entities</c> of the role that governs the kind (0 when
/// the caller has no such role, null when the kind is unlimited) and
/// <see cref="Used"/> the number of entities currently followed.
/// </summary>
public sealed record SubscriptionLimit(string Kind, int? Max, int Used)
{
    /// <summary>Whether another entity of this kind can be followed.</summary>
    public bool CanAddMore => Max is null || Used < Max;
}
