using System.Text.Json;

namespace BestAgent.Application.Tools;

public static class ToolInputPathHelper
{
    public static JsonDocument? TryParseJson(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(payload);
        }
        catch
        {
            return null;
        }
    }

    public static bool TryResolvePath(JsonElement root, string inputPath, out JsonElement value)
    {
        value = root;
        foreach (var segment in inputPath.Split('.', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                if (!TryGetProperty(value, segment, out value))
                {
                    return false;
                }

                continue;
            }

            if (value.ValueKind == JsonValueKind.Array
                && int.TryParse(segment, out var index)
                && index >= 0
                && index < value.GetArrayLength())
            {
                value = value[index];
                continue;
            }

            return false;
        }

        return true;
    }

    public static IReadOnlyList<string> EnumerateLeafPaths(JsonElement root)
    {
        var paths = new List<string>();
        EnumerateLeafPaths(root, string.Empty, paths);
        return paths;
    }

    private static void EnumerateLeafPaths(JsonElement value, string currentPath, ICollection<string> paths)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var hadChildren = false;
                foreach (var property in value.EnumerateObject())
                {
                    hadChildren = true;
                    var childPath = string.IsNullOrWhiteSpace(currentPath)
                        ? property.Name
                        : $"{currentPath}.{property.Name}";
                    EnumerateLeafPaths(property.Value, childPath, paths);
                }

                if (!hadChildren && !string.IsNullOrWhiteSpace(currentPath))
                {
                    paths.Add(currentPath);
                }

                break;
            }
            case JsonValueKind.Array:
            {
                var index = 0;
                var hadChildren = false;
                foreach (var item in value.EnumerateArray())
                {
                    hadChildren = true;
                    var childPath = string.IsNullOrWhiteSpace(currentPath)
                        ? index.ToString()
                        : $"{currentPath}.{index}";
                    EnumerateLeafPaths(item, childPath, paths);
                    index++;
                }

                if (!hadChildren && !string.IsNullOrWhiteSpace(currentPath))
                {
                    paths.Add(currentPath);
                }

                break;
            }
            default:
                if (!string.IsNullOrWhiteSpace(currentPath))
                {
                    paths.Add(currentPath);
                }

                break;
        }
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
