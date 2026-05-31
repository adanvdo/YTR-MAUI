namespace YTR.Core.Enums;

/// <summary>
/// How the segment extraction control specifies the end point.
/// </summary>
public enum SegmentMode
{
    /// <summary>User specifies a duration after the start time.</summary>
    Duration = 0,
    /// <summary>User specifies an absolute end time.</summary>
    EndTime = 1
}
