namespace TripleDetection.Domain.Enums;

public enum ValidType { Year = 0, Month = 1, Day = 2 }
public enum ProductStatus { Inactive = 0, Active = 1 }
public enum TaskStatus { Pending = 0, Approved = 1, Running = 2, Completed = 3 }

/// <summary>
/// NG 缺陷分类（OK 记录为 None）。多缺陷并存时取优先级最高者，全部明细存 NgDetail。
/// </summary>
public enum NgReason
{
    None = 0,
    BnMismatch = 1,
    MfgMismatch = 2,
    ExpMismatch = 3,
    BnMissing = 10,
    MfgMissing = 11,
    ExpMissing = 12,
    OcrFailed = 20,
    Timeout = 21,
    ImageQuality = 22,
    Other = 99
}