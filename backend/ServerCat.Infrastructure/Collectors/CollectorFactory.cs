using Microsoft.Extensions.DependencyInjection;
using ServerCat.Core.Interfaces;

namespace ServerCat.Infrastructure.Collectors;

/// <summary>
/// Resolves the correct ICollector implementation for a given collector type string.
/// New collectors can be registered in DI and picked up here without changing other code.
/// </summary>
public class CollectorFactory(IServiceProvider serviceProvider)
{
    public ICollector Create(string collectorType)
    {
        var collectors = serviceProvider.GetServices<ICollector>();
        var collector = collectors.FirstOrDefault(c =>
            string.Equals(c.CollectorType, collectorType, StringComparison.OrdinalIgnoreCase));

        if (collector == null)
            throw new InvalidOperationException(
                $"No collector registered for type '{collectorType}'. " +
                $"Registered types: {string.Join(", ", collectors.Select(c => c.CollectorType))}");

        return collector;
    }
}
