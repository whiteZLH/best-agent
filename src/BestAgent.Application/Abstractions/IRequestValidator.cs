namespace BestAgent.Application.Abstractions;

public interface IRequestValidator<in TRequest>
{
    IEnumerable<string> Validate(TRequest request);
}
