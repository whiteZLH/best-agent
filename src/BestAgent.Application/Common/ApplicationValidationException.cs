namespace BestAgent.Application.Common;

public sealed class ApplicationValidationException : Exception
{
    public ApplicationValidationException(IEnumerable<string> errors)
        : base("Request validation failed.")
    {
        Errors = errors.ToArray();
    }

    public IReadOnlyList<string> Errors { get; }
}
