namespace BestAgent.Application.Common;

public static class IdGenerator
{
    public static string New(string prefix)
    {
        return $"{prefix}_{Guid.NewGuid():N}";
    }
}
