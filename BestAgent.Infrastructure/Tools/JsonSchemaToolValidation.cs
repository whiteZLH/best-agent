using System.Text.Json;
using System.Globalization;
using System.Net.Mail;
using System.Text.RegularExpressions;

namespace BestAgent.Infrastructure.Tools;

internal static class JsonSchemaToolValidation
{
    public static JsonElement ParseSchema(string toolName, string schema, string schemaKind)
    {
        using var schemaDocument = JsonDocument.Parse(schema);
        if (schemaDocument.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"{schemaKind} schema for tool '{toolName}' must be a JSON object.");
        }

        return schemaDocument.RootElement.Clone();
    }

    public static bool TryValidateElement(
        string toolName,
        string path,
        JsonElement value,
        JsonElement schema,
        out string? error,
        string payloadKind)
    {
        error = null;
        if (schema.ValueKind != JsonValueKind.Object)
        {
            return true;
        }

        if (schema.TryGetProperty("const", out var constProperty)
            && !JsonElementEquals(value, constProperty))
        {
            error = $"{payloadKind} for tool '{toolName}' at '{path}' must match const value.";
            return false;
        }

        if (!TryValidateCompositeKeyword(toolName, path, value, schema, "allOf", payloadKind, out error))
        {
            return false;
        }

        if (!TryValidateCompositeKeyword(toolName, path, value, schema, "anyOf", payloadKind, out error))
        {
            return false;
        }

        if (!TryValidateCompositeKeyword(toolName, path, value, schema, "oneOf", payloadKind, out error))
        {
            return false;
        }

        if (schema.TryGetProperty("not", out var notProperty)
            && notProperty.ValueKind == JsonValueKind.Object
            && TryValidateElement(toolName, path, value, notProperty, out _, payloadKind))
        {
            error = $"{payloadKind} for tool '{toolName}' at '{path}' must not match schema in 'not'.";
            return false;
        }

        if (schema.TryGetProperty("type", out var typeProperty))
        {
            var allowedTypes = ReadAllowedTypes(typeProperty);
            if (allowedTypes.Count > 0 && !allowedTypes.Any(type => MatchesType(value, type)))
            {
                error = $"{payloadKind} for tool '{toolName}' at '{path}' must be {string.Join(" or ", allowedTypes)}.";
                return false;
            }
        }

        if (schema.TryGetProperty("enum", out var enumProperty)
            && enumProperty.ValueKind == JsonValueKind.Array
            && !enumProperty.EnumerateArray().Any(candidate => JsonElementEquals(value, candidate)))
        {
            error = $"{payloadKind} for tool '{toolName}' at '{path}' is not an allowed enum value.";
            return false;
        }

        if (schema.TryGetProperty("format", out var formatProperty)
            && formatProperty.ValueKind == JsonValueKind.String)
        {
            var format = formatProperty.GetString();
            if (!string.IsNullOrWhiteSpace(format))
            {
                if (value.ValueKind != JsonValueKind.String)
                {
                    error = $"{payloadKind} for tool '{toolName}' at '{path}' must be string.";
                    return false;
                }

                var stringValue = value.GetString() ?? string.Empty;
                if (!MatchesFormat(stringValue, format))
                {
                    error = $"{payloadKind} for tool '{toolName}' at '{path}' must match format '{format}'.";
                    return false;
                }
            }
        }

        if (schema.TryGetProperty("minLength", out var minLengthProperty)
            && TryReadNonNegativeInt(minLengthProperty, out var minLength))
        {
            if (value.ValueKind != JsonValueKind.String)
            {
                error = $"{payloadKind} for tool '{toolName}' at '{path}' must be string.";
                return false;
            }

            var stringValue = value.GetString() ?? string.Empty;
            if (stringValue.Length < minLength)
            {
                error = $"{payloadKind} for tool '{toolName}' at '{path}' must have length >= {minLength}.";
                return false;
            }
        }

        if (schema.TryGetProperty("maxLength", out var maxLengthProperty)
            && TryReadNonNegativeInt(maxLengthProperty, out var maxLength))
        {
            if (value.ValueKind != JsonValueKind.String)
            {
                error = $"{payloadKind} for tool '{toolName}' at '{path}' must be string.";
                return false;
            }

            var stringValue = value.GetString() ?? string.Empty;
            if (stringValue.Length > maxLength)
            {
                error = $"{payloadKind} for tool '{toolName}' at '{path}' must have length <= {maxLength}.";
                return false;
            }
        }

        if (schema.TryGetProperty("minimum", out var minimumProperty)
            && TryReadDecimal(minimumProperty, out var minimumValue))
        {
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var actualValue))
            {
                error = $"{payloadKind} for tool '{toolName}' at '{path}' must be number.";
                return false;
            }

            if (actualValue < minimumValue)
            {
                error = $"{payloadKind} for tool '{toolName}' at '{path}' must be >= {minimumValue}.";
                return false;
            }
        }

        if (schema.TryGetProperty("exclusiveMinimum", out var exclusiveMinimumProperty)
            && TryReadDecimal(exclusiveMinimumProperty, out var exclusiveMinimumValue))
        {
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var actualValue))
            {
                error = $"{payloadKind} for tool '{toolName}' at '{path}' must be number.";
                return false;
            }

            if (actualValue <= exclusiveMinimumValue)
            {
                error = $"{payloadKind} for tool '{toolName}' at '{path}' must be > {exclusiveMinimumValue}.";
                return false;
            }
        }

        if (schema.TryGetProperty("maximum", out var maximumProperty)
            && TryReadDecimal(maximumProperty, out var maximumValue))
        {
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var actualValue))
            {
                error = $"{payloadKind} for tool '{toolName}' at '{path}' must be number.";
                return false;
            }

            if (actualValue > maximumValue)
            {
                error = $"{payloadKind} for tool '{toolName}' at '{path}' must be <= {maximumValue}.";
                return false;
            }
        }

        if (schema.TryGetProperty("exclusiveMaximum", out var exclusiveMaximumProperty)
            && TryReadDecimal(exclusiveMaximumProperty, out var exclusiveMaximumValue))
        {
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var actualValue))
            {
                error = $"{payloadKind} for tool '{toolName}' at '{path}' must be number.";
                return false;
            }

            if (actualValue >= exclusiveMaximumValue)
            {
                error = $"{payloadKind} for tool '{toolName}' at '{path}' must be < {exclusiveMaximumValue}.";
                return false;
            }
        }

        if (schema.TryGetProperty("multipleOf", out var multipleOfProperty)
            && TryReadPositiveDecimal(multipleOfProperty, out var multipleOfValue))
        {
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var actualValue))
            {
                error = $"{payloadKind} for tool '{toolName}' at '{path}' must be number.";
                return false;
            }

            if (actualValue % multipleOfValue != 0)
            {
                error = $"{payloadKind} for tool '{toolName}' at '{path}' must be a multiple of {multipleOfValue}.";
                return false;
            }
        }

        if (schema.TryGetProperty("minItems", out var minItemsProperty)
            && TryReadNonNegativeInt(minItemsProperty, out var minItems))
        {
            if (value.ValueKind != JsonValueKind.Array)
            {
                error = $"{payloadKind} for tool '{toolName}' at '{path}' must be array.";
                return false;
            }

            var itemCount = value.GetArrayLength();
            if (itemCount < minItems)
            {
                error = $"{payloadKind} for tool '{toolName}' at '{path}' must have at least {minItems} items.";
                return false;
            }
        }

        if (schema.TryGetProperty("minProperties", out var minPropertiesProperty)
            && TryReadNonNegativeInt(minPropertiesProperty, out var minProperties))
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                error = $"{payloadKind} for tool '{toolName}' at '{path}' must be object.";
                return false;
            }

            var propertyCount = value.EnumerateObject().Count();
            if (propertyCount < minProperties)
            {
                error = $"{payloadKind} for tool '{toolName}' at '{path}' must have at least {minProperties} properties.";
                return false;
            }
        }

        if (schema.TryGetProperty("maxProperties", out var maxPropertiesProperty)
            && TryReadNonNegativeInt(maxPropertiesProperty, out var maxProperties))
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                error = $"{payloadKind} for tool '{toolName}' at '{path}' must be object.";
                return false;
            }

            var propertyCount = value.EnumerateObject().Count();
            if (propertyCount > maxProperties)
            {
                error = $"{payloadKind} for tool '{toolName}' at '{path}' must have at most {maxProperties} properties.";
                return false;
            }
        }

        if (schema.TryGetProperty("maxItems", out var maxItemsProperty)
            && TryReadNonNegativeInt(maxItemsProperty, out var maxItems))
        {
            if (value.ValueKind != JsonValueKind.Array)
            {
                error = $"{payloadKind} for tool '{toolName}' at '{path}' must be array.";
                return false;
            }

            var itemCount = value.GetArrayLength();
            if (itemCount > maxItems)
            {
                error = $"{payloadKind} for tool '{toolName}' at '{path}' must have at most {maxItems} items.";
                return false;
            }
        }

        if (schema.TryGetProperty("contains", out var containsProperty)
            && containsProperty.ValueKind == JsonValueKind.Object)
        {
            if (value.ValueKind != JsonValueKind.Array)
            {
                error = $"{payloadKind} for tool '{toolName}' at '{path}' must be array.";
                return false;
            }

            var matchingItemCount = 0;
            foreach (var item in value.EnumerateArray())
            {
                if (TryValidateElement(
                    toolName,
                    path,
                    item,
                    containsProperty,
                    out _,
                    payloadKind))
                {
                    matchingItemCount++;
                }
            }

            var minContains = 1;
            if (schema.TryGetProperty("minContains", out var minContainsProperty)
                && TryReadNonNegativeInt(minContainsProperty, out var configuredMinContains))
            {
                minContains = configuredMinContains;
            }

            if (matchingItemCount < minContains)
            {
                error = minContains == 1
                    ? $"{payloadKind} for tool '{toolName}' at '{path}' must contain at least one item matching schema in 'contains'."
                    : $"{payloadKind} for tool '{toolName}' at '{path}' must contain at least {minContains} items matching schema in 'contains'.";
                return false;
            }

            if (schema.TryGetProperty("maxContains", out var maxContainsProperty)
                && TryReadNonNegativeInt(maxContainsProperty, out var maxContains)
                && matchingItemCount > maxContains)
            {
                error = $"{payloadKind} for tool '{toolName}' at '{path}' must contain at most {maxContains} items matching schema in 'contains'.";
                return false;
            }
        }

        if (schema.TryGetProperty("uniqueItems", out var uniqueItemsProperty)
            && uniqueItemsProperty.ValueKind == JsonValueKind.True)
        {
            if (value.ValueKind != JsonValueKind.Array)
            {
                error = $"{payloadKind} for tool '{toolName}' at '{path}' must be array.";
                return false;
            }

            var seenItems = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in value.EnumerateArray())
            {
                if (!seenItems.Add(item.GetRawText()))
                {
                    error = $"{payloadKind} for tool '{toolName}' at '{path}' must contain unique items.";
                    return false;
                }
            }
        }

        var tuplePrefixCount = 0;
        var usesLegacyTupleItems = false;
        if (schema.TryGetProperty("prefixItems", out var prefixItemsProperty)
            && prefixItemsProperty.ValueKind == JsonValueKind.Array)
        {
            if (value.ValueKind != JsonValueKind.Array)
            {
                error = $"{payloadKind} for tool '{toolName}' at '{path}' must be array.";
                return false;
            }

            tuplePrefixCount = prefixItemsProperty.GetArrayLength();
            var index = 0;
            foreach (var tupleSchema in prefixItemsProperty.EnumerateArray())
            {
                if (index >= value.GetArrayLength())
                {
                    break;
                }

                if (tupleSchema.ValueKind == JsonValueKind.Object
                    && !TryValidateElement(
                        toolName,
                        $"{path}[{index}]",
                        value[index],
                        tupleSchema,
                        out error,
                        payloadKind))
                {
                    return false;
                }

                index++;
            }
        }
        else if (schema.TryGetProperty("items", out var tupleItemsProperty)
                 && tupleItemsProperty.ValueKind == JsonValueKind.Array)
        {
            usesLegacyTupleItems = true;
            if (value.ValueKind != JsonValueKind.Array)
            {
                error = $"{payloadKind} for tool '{toolName}' at '{path}' must be array.";
                return false;
            }

            tuplePrefixCount = tupleItemsProperty.GetArrayLength();
            var index = 0;
            foreach (var tupleSchema in tupleItemsProperty.EnumerateArray())
            {
                if (index >= value.GetArrayLength())
                {
                    break;
                }

                if (tupleSchema.ValueKind == JsonValueKind.Object
                    && !TryValidateElement(
                        toolName,
                        $"{path}[{index}]",
                        value[index],
                        tupleSchema,
                        out error,
                        payloadKind))
                {
                    return false;
                }

                index++;
            }

        }

        if (tuplePrefixCount > 0
            && (!schema.TryGetProperty("items", out var trailingItemsSchema)
                || trailingItemsSchema.ValueKind != JsonValueKind.Object
                || usesLegacyTupleItems)
            && !TryValidateAdditionalTupleItems(
                toolName,
                path,
                value,
                schema,
                tuplePrefixCount,
                payloadKind,
                out error))
        {
            return false;
        }

        if (schema.TryGetProperty("items", out var itemsProperty)
            && itemsProperty.ValueKind == JsonValueKind.Object)
        {
            if (value.ValueKind != JsonValueKind.Array)
            {
                error = $"{payloadKind} for tool '{toolName}' at '{path}' must be array.";
                return false;
            }

            var index = 0;
            foreach (var item in value.EnumerateArray())
            {
                if (index < tuplePrefixCount)
                {
                    index++;
                    continue;
                }

                if (!TryValidateElement(
                    toolName,
                    $"{path}[{index}]",
                    item,
                    itemsProperty,
                    out error,
                    payloadKind))
                {
                    return false;
                }

                index++;
            }
        }

        if (tuplePrefixCount > 0
            && schema.TryGetProperty("items", out var trailingItemsProperty)
            && trailingItemsProperty.ValueKind == JsonValueKind.False
            && value.ValueKind == JsonValueKind.Array
            && value.GetArrayLength() > tuplePrefixCount)
        {
            error = $"{payloadKind} for tool '{toolName}' at '{path}[{tuplePrefixCount}]' is not allowed by tuple schema.";
            return false;
        }

        if (schema.TryGetProperty("required", out var requiredProperty)
            && requiredProperty.ValueKind == JsonValueKind.Array)
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                error = $"{payloadKind} for tool '{toolName}' at '{path}' must be object.";
                return false;
            }

            foreach (var required in requiredProperty.EnumerateArray())
            {
                var propertyName = required.ValueKind == JsonValueKind.String ? required.GetString() : null;
                if (!string.IsNullOrWhiteSpace(propertyName) && !value.TryGetProperty(propertyName, out _))
                {
                    error = $"{payloadKind} for tool '{toolName}' is missing required property '{propertyName}'.";
                    return false;
                }
            }
        }

        if (schema.TryGetProperty("dependentRequired", out var dependentRequiredProperty)
            && dependentRequiredProperty.ValueKind == JsonValueKind.Object)
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                error = $"{payloadKind} for tool '{toolName}' at '{path}' must be object.";
                return false;
            }

            foreach (var dependency in dependentRequiredProperty.EnumerateObject())
            {
                if (!value.TryGetProperty(dependency.Name, out _)
                    || dependency.Value.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var requiredDependency in dependency.Value.EnumerateArray())
                {
                    var requiredPropertyName = requiredDependency.ValueKind == JsonValueKind.String
                        ? requiredDependency.GetString()
                        : null;
                    if (!string.IsNullOrWhiteSpace(requiredPropertyName)
                        && !value.TryGetProperty(requiredPropertyName, out _))
                    {
                        error = $"{payloadKind} for tool '{toolName}' is missing dependent property '{requiredPropertyName}' required by '{dependency.Name}'.";
                        return false;
                    }
                }
            }
        }

        if (schema.TryGetProperty("dependentSchemas", out var dependentSchemasProperty)
            && dependentSchemasProperty.ValueKind == JsonValueKind.Object)
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                error = $"{payloadKind} for tool '{toolName}' at '{path}' must be object.";
                return false;
            }

            foreach (var dependency in dependentSchemasProperty.EnumerateObject())
            {
                if (!value.TryGetProperty(dependency.Name, out _)
                    || dependency.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!TryValidateElement(
                        toolName,
                        path,
                        value,
                        dependency.Value,
                        out error,
                        payloadKind))
                {
                    return false;
                }
            }
        }

        if (schema.TryGetProperty("propertyNames", out var propertyNamesProperty)
            && propertyNamesProperty.ValueKind == JsonValueKind.Object)
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                error = $"{payloadKind} for tool '{toolName}' at '{path}' must be object.";
                return false;
            }

            foreach (var inputProperty in value.EnumerateObject())
            {
                using var propertyNameDocument = JsonDocument.Parse(JsonSerializer.Serialize(inputProperty.Name));
                if (!TryValidateElement(
                        toolName,
                        $"{path}.<{inputProperty.Name}>",
                        propertyNameDocument.RootElement,
                        propertyNamesProperty,
                        out error,
                        payloadKind))
                {
                    return false;
                }
            }
        }

        JsonElement? patternPropertiesSchema = null;
        if (schema.TryGetProperty("patternProperties", out var patternPropertiesProperty)
            && patternPropertiesProperty.ValueKind == JsonValueKind.Object)
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                error = $"{payloadKind} for tool '{toolName}' at '{path}' must be object.";
                return false;
            }

            patternPropertiesSchema = patternPropertiesProperty;
            foreach (var inputProperty in value.EnumerateObject())
            {
                foreach (var patternSchema in patternPropertiesProperty.EnumerateObject())
                {
                    if (!Regex.IsMatch(inputProperty.Name, patternSchema.Name, RegexOptions.CultureInvariant))
                    {
                        continue;
                    }

                    if (!TryValidateElement(
                            toolName,
                            $"{path}.{inputProperty.Name}",
                            inputProperty.Value,
                            patternSchema.Value,
                            out error,
                            payloadKind))
                    {
                        return false;
                    }
                }
            }
        }

        JsonElement? propertiesSchema = null;
        if (schema.TryGetProperty("properties", out var propertiesProperty)
            && propertiesProperty.ValueKind == JsonValueKind.Object)
        {
            propertiesSchema = propertiesProperty;
            if (value.ValueKind != JsonValueKind.Object)
            {
                return true;
            }

            foreach (var schemaProperty in propertiesProperty.EnumerateObject())
            {
                if (value.TryGetProperty(schemaProperty.Name, out var propertyValue)
                    && !TryValidateElement(
                        toolName,
                        $"{path}.{schemaProperty.Name}",
                        propertyValue,
                        schemaProperty.Value,
                        out error,
                        payloadKind))
                {
                    return false;
                }
            }
        }

        if (schema.TryGetProperty("pattern", out var patternProperty)
            && patternProperty.ValueKind == JsonValueKind.String)
        {
            if (value.ValueKind != JsonValueKind.String)
            {
                error = $"{payloadKind} for tool '{toolName}' at '{path}' must be string.";
                return false;
            }

            var pattern = patternProperty.GetString();
            if (!string.IsNullOrWhiteSpace(pattern)
                && !Regex.IsMatch(value.GetString() ?? string.Empty, pattern, RegexOptions.CultureInvariant))
            {
                error = $"{payloadKind} for tool '{toolName}' at '{path}' must match pattern '{pattern}'.";
                return false;
            }
        }

        if (schema.TryGetProperty("if", out var ifProperty)
            && ifProperty.ValueKind == JsonValueKind.Object)
        {
            var ifMatched = TryValidateElement(toolName, path, value, ifProperty, out _, payloadKind);
            if (ifMatched
                && schema.TryGetProperty("then", out var thenProperty)
                && thenProperty.ValueKind == JsonValueKind.Object
                && !TryValidateElement(toolName, path, value, thenProperty, out error, payloadKind))
            {
                return false;
            }

            if (!ifMatched
                && schema.TryGetProperty("else", out var elseProperty)
                && elseProperty.ValueKind == JsonValueKind.Object
                && !TryValidateElement(toolName, path, value, elseProperty, out error, payloadKind))
            {
                return false;
            }
        }

        if (schema.TryGetProperty("additionalProperties", out var additionalPropertiesProperty)
            && value.ValueKind == JsonValueKind.Object
            && additionalPropertiesProperty.ValueKind is JsonValueKind.False or JsonValueKind.Object)
        {
            foreach (var inputProperty in value.EnumerateObject())
            {
                if (IsDeclaredProperty(inputProperty.Name, propertiesSchema)
                    || MatchesPatternProperty(inputProperty.Name, patternPropertiesSchema))
                {
                    continue;
                }

                if (additionalPropertiesProperty.ValueKind == JsonValueKind.False)
                {
                    error = $"{payloadKind} for tool '{toolName}' contains unexpected property '{inputProperty.Name}'.";
                    return false;
                }

                if (!TryValidateElement(
                        toolName,
                        $"{path}.{inputProperty.Name}",
                        inputProperty.Value,
                        additionalPropertiesProperty,
                        out error,
                        payloadKind))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool TryValidateAdditionalTupleItems(
        string toolName,
        string path,
        JsonElement value,
        JsonElement schema,
        int tuplePrefixCount,
        string payloadKind,
        out string? error)
    {
        error = null;

        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() <= tuplePrefixCount)
        {
            return true;
        }

        if (!schema.TryGetProperty("additionalItems", out var additionalItemsProperty))
        {
            return true;
        }

        if (additionalItemsProperty.ValueKind == JsonValueKind.False)
        {
            error = $"{payloadKind} for tool '{toolName}' at '{path}[{tuplePrefixCount}]' is not allowed by tuple schema.";
            return false;
        }

        if (additionalItemsProperty.ValueKind != JsonValueKind.Object)
        {
            return true;
        }

        for (var index = tuplePrefixCount; index < value.GetArrayLength(); index++)
        {
            if (!TryValidateElement(
                    toolName,
                    $"{path}[{index}]",
                    value[index],
                    additionalItemsProperty,
                    out error,
                    payloadKind))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryValidateCompositeKeyword(
        string toolName,
        string path,
        JsonElement value,
        JsonElement schema,
        string keyword,
        string payloadKind,
        out string? error)
    {
        error = null;
        if (!schema.TryGetProperty(keyword, out var keywordProperty)
            || keywordProperty.ValueKind != JsonValueKind.Array)
        {
            return true;
        }

        var matchCount = 0;
        string? firstCandidateError = null;

        foreach (var candidateSchema in keywordProperty.EnumerateArray())
        {
            if (candidateSchema.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (TryValidateElement(toolName, path, value, candidateSchema, out var candidateError, payloadKind))
            {
                matchCount++;
            }
            else if (firstCandidateError is null)
            {
                firstCandidateError = candidateError;
            }

            if (string.Equals(keyword, "allOf", StringComparison.Ordinal))
            {
                if (candidateError is not null)
                {
                    error = candidateError;
                    return false;
                }
            }
        }

        if (string.Equals(keyword, "allOf", StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(keyword, "anyOf", StringComparison.Ordinal))
        {
            if (matchCount > 0)
            {
                return true;
            }

            error = firstCandidateError
                ?? $"{payloadKind} for tool '{toolName}' at '{path}' must match at least one schema in '{keyword}'.";
            return false;
        }

        if (string.Equals(keyword, "oneOf", StringComparison.Ordinal))
        {
            if (matchCount == 1)
            {
                return true;
            }

            error = matchCount > 1
                ? $"{payloadKind} for tool '{toolName}' at '{path}' must match exactly one schema in '{keyword}'."
                : firstCandidateError ?? $"{payloadKind} for tool '{toolName}' at '{path}' must match exactly one schema in '{keyword}'.";
            return false;
        }

        return true;
    }

    private static IReadOnlyList<string> ReadAllowedTypes(JsonElement typeProperty)
    {
        if (typeProperty.ValueKind == JsonValueKind.String)
        {
            var type = typeProperty.GetString();
            return string.IsNullOrWhiteSpace(type) ? [] : [type.Trim()];
        }

        if (typeProperty.ValueKind == JsonValueKind.Array)
        {
            return typeProperty
                .EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                .Select(item => item.GetString()!.Trim())
                .ToArray();
        }

        return [];
    }

    private static bool MatchesType(JsonElement value, string type)
    {
        return type switch
        {
            "object" => value.ValueKind == JsonValueKind.Object,
            "array" => value.ValueKind == JsonValueKind.Array,
            "string" => value.ValueKind == JsonValueKind.String,
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            "number" => value.ValueKind == JsonValueKind.Number,
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "null" => value.ValueKind == JsonValueKind.Null,
            _ => true
        };
    }

    private static bool JsonElementEquals(JsonElement left, JsonElement right)
    {
        return left.ValueKind == right.ValueKind
            && left.GetRawText() == right.GetRawText();
    }

    private static bool MatchesPatternProperty(string propertyName, JsonElement? patternPropertiesSchema)
    {
        if (patternPropertiesSchema is null || patternPropertiesSchema.Value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var patternSchema in patternPropertiesSchema.Value.EnumerateObject())
        {
            if (Regex.IsMatch(propertyName, patternSchema.Name, RegexOptions.CultureInvariant))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDeclaredProperty(string propertyName, JsonElement? propertiesSchema)
    {
        if (propertiesSchema is null || propertiesSchema.Value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        return propertiesSchema.Value.TryGetProperty(propertyName, out _);
    }

    private static bool TryReadNonNegativeInt(JsonElement element, out int value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Number
            && element.TryGetInt32(out value)
            && value >= 0;
    }

    private static bool TryReadDecimal(JsonElement element, out decimal value)
    {
        value = 0m;
        return element.ValueKind == JsonValueKind.Number
            && element.TryGetDecimal(out value);
    }

    private static bool TryReadPositiveDecimal(JsonElement element, out decimal value)
    {
        value = 0m;
        return element.ValueKind == JsonValueKind.Number
            && element.TryGetDecimal(out value)
            && value > 0m;
    }

    private static bool MatchesFormat(string value, string format)
    {
        return format.Trim().ToLowerInvariant() switch
        {
            "email" => MatchesEmail(value),
            "uri" => Uri.TryCreate(value, UriKind.Absolute, out _),
            "date-time" => DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out _),
            "date" => DateOnly.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _),
            "uuid" => Guid.TryParse(value, out _),
            _ => true
        };
    }

    private static bool MatchesEmail(string value)
    {
        try
        {
            var address = new MailAddress(value);
            return string.Equals(address.Address, value, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
