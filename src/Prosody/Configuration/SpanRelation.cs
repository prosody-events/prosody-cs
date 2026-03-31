namespace Prosody.Configuration;

/// <summary>
/// Controls how a new span relates to a propagated OpenTelemetry context.
/// </summary>
public enum SpanRelation
{
    /// <summary>
    /// The propagated span becomes this span's OTel parent (child-of relationship).
    /// The execution span is part of the same trace as the producer.
    /// </summary>
    Child = 0,

    /// <summary>
    /// The propagated span is added as an OTel link; this span starts a new trace
    /// root (follows-from relationship). The execution span is causally related but
    /// not part of the same operation.
    /// </summary>
    FollowsFrom = 1,
}
