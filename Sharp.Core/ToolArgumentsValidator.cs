using System.Text.Json;
using System.Globalization;
using System.Net.Mail;
using System.Text.RegularExpressions;

namespace Sharp.Core;

internal static class ToolArgumentsValidator
{
    public static bool TryValidate(
        JsonElement schema,
        JsonElement arguments,
        out string error)
    {
        return ValidateValue(schema, arguments, "$", out error);
    }

    private static bool ValidateValue(JsonElement schema, JsonElement value, string path, out string error)
    {
        error = string.Empty;
        if (schema.ValueKind != JsonValueKind.Object)
            return true;

        if (TryGetSchemaProperty(schema, "allOf", "all_of", out var allOf)
            && allOf.ValueKind == JsonValueKind.Array)
        {
            foreach (var variant in allOf.EnumerateArray())
            {
                if (!ValidateValue(variant, value, path, out error))
                    return false;
            }
        }

        if (TryGetSchemaProperty(schema, "anyOf", "any_of", out var anyOf)
            && anyOf.ValueKind == JsonValueKind.Array)
        {
            var matchedAny = anyOf.EnumerateArray().Any(variant => ValidateValue(variant, value, path, out _));
            if (!matchedAny)
            {
                error = $"{path} must match at least one schema in anyOf.";
                return false;
            }
        }

        if (TryGetSchemaProperty(schema, "const", out var constValue)
            && !JsonElement.DeepEquals(constValue, value))
        {
            error = $"{path} must equal the schema const value.";
            return false;
        }

        if (TryGetSchemaProperty(schema, "enum", out var enumValues)
            && enumValues.ValueKind == JsonValueKind.Array)
        {
            var matches = enumValues.EnumerateArray().Any(candidate => JsonElement.DeepEquals(candidate, value));
            if (!matches)
            {
                error = $"{path} must match one of the schema enum values.";
                return false;
            }
        }

        if (TryGetSchemaProperty(schema, "oneOf", "one_of", out var oneOf)
            && oneOf.ValueKind == JsonValueKind.Array)
        {
            var matchedCount = 0;
            foreach (var variant in oneOf.EnumerateArray())
            {
                if (ValidateValue(variant, value, path, out _))
                    matchedCount++;
            }

            if (matchedCount != 1)
            {
                error = $"{path} must match exactly one schema in oneOf.";
                return false;
            }
        }

        if (TryGetSchemaProperty(schema, "not", out var notSchema)
            && ValidateValue(notSchema, value, path, out _))
        {
            error = $"{path} must not match the schema in not.";
            return false;
        }

        if (!MatchesDeclaredType(schema, value))
        {
            error = $"{path} does not match expected type '{DescribeType(schema)}'.";
            return false;
        }

        if (!ValidateScalarConstraints(schema, value, path, out error))
            return false;

        if (value.ValueKind == JsonValueKind.Object)
            return ValidateObject(schema, value, path, out error);

        if (value.ValueKind == JsonValueKind.Array)
            return ValidateArray(schema, value, path, out error);

        return true;
    }

    private static bool ValidateScalarConstraints(JsonElement schema, JsonElement value, string path, out string error)
    {
        error = string.Empty;

        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString() ?? string.Empty;
            if (TryGetIntConstraint(schema, "minLength", out var minLength) && text.Length < minLength)
            {
                error = $"{path} string length must be >= {minLength}.";
                return false;
            }

            if (TryGetIntConstraint(schema, "maxLength", out var maxLength) && text.Length > maxLength)
            {
                error = $"{path} string length must be <= {maxLength}.";
                return false;
            }

            if (TryGetSchemaProperty(schema, "pattern", out var patternElement)
                && patternElement.ValueKind == JsonValueKind.String)
            {
                var pattern = patternElement.GetString();
                if (!string.IsNullOrEmpty(pattern)
                    && !Regex.IsMatch(text, pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200)))
                {
                    error = $"{path} must match pattern '{pattern}'.";
                    return false;
                }
            }

            if (TryGetSchemaProperty(schema, "format", out var formatElement)
                && formatElement.ValueKind == JsonValueKind.String)
            {
                var format = (formatElement.GetString() ?? string.Empty).Trim().ToLowerInvariant();
                if (!string.IsNullOrEmpty(format) && !MatchesStringFormat(text, format))
                {
                    error = $"{path} must match format '{format}'.";
                    return false;
                }
            }
        }

        if (value.ValueKind == JsonValueKind.Number)
        {
            if (!value.TryGetDecimal(out var numericValue))
                return true;

            if (TryGetDecimalConstraint(schema, "minimum", out var minimum) && numericValue < minimum)
            {
                error = $"{path} must be >= {minimum.ToString(CultureInfo.InvariantCulture)}.";
                return false;
            }

            if (TryGetDecimalConstraint(schema, "maximum", out var maximum) && numericValue > maximum)
            {
                error = $"{path} must be <= {maximum.ToString(CultureInfo.InvariantCulture)}.";
                return false;
            }

            if (TryGetExclusiveMinimum(schema, out var exclusiveMinimum) && numericValue <= exclusiveMinimum)
            {
                error = $"{path} must be > {exclusiveMinimum.ToString(CultureInfo.InvariantCulture)}.";
                return false;
            }

            if (TryGetExclusiveMaximum(schema, out var exclusiveMaximum) && numericValue >= exclusiveMaximum)
            {
                error = $"{path} must be < {exclusiveMaximum.ToString(CultureInfo.InvariantCulture)}.";
                return false;
            }
        }

        return true;
    }

    private static bool ValidateObject(JsonElement schema, JsonElement value, string path, out string error)
    {
        error = string.Empty;
        if (TryGetIntConstraint(schema, "minProperties", out var minProperties)
            && value.EnumerateObject().Count() < minProperties)
        {
            error = $"{path} must contain at least {minProperties} properties.";
            return false;
        }

        if (TryGetIntConstraint(schema, "maxProperties", out var maxProperties)
            && value.EnumerateObject().Count() > maxProperties)
        {
            error = $"{path} must contain at most {maxProperties} properties.";
            return false;
        }

        var required = ReadRequiredProperties(schema);
        foreach (var property in required)
        {
            if (!value.TryGetProperty(property, out _))
            {
                error = $"{path} is missing required argument '{property}'.";
                return false;
            }
        }

        var additionalPropertiesAllowed = ReadAdditionalPropertiesAllowed(schema, out var additionalPropertiesSchema);
        var propertySchemas = ReadPropertySchemas(schema);

        foreach (var argument in value.EnumerateObject())
        {
            var propertyPath = $"{path}.{argument.Name}";
            if (propertySchemas.TryGetValue(argument.Name, out var propertySchema))
            {
                if (!ValidateValue(propertySchema, argument.Value, propertyPath, out error))
                    return false;
                continue;
            }

            if (!additionalPropertiesAllowed)
            {
                error = $"Unknown argument '{argument.Name}' is not allowed by schema.";
                return false;
            }

            if (additionalPropertiesSchema is { } schemaForAdditional
                && !ValidateValue(schemaForAdditional, argument.Value, propertyPath, out error))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ValidateArray(JsonElement schema, JsonElement value, string path, out string error)
    {
        error = string.Empty;
        var itemCount = value.GetArrayLength();
        if (TryGetIntConstraint(schema, "minItems", out var minItems) && itemCount < minItems)
        {
            error = $"{path} must contain at least {minItems} items.";
            return false;
        }

        if (TryGetIntConstraint(schema, "maxItems", out var maxItems) && itemCount > maxItems)
        {
            error = $"{path} must contain at most {maxItems} items.";
            return false;
        }

        if (!TryGetSchemaProperty(schema, "items", out var itemsSchema))
            return true;

        if (itemsSchema.ValueKind == JsonValueKind.Object)
        {
            var index = 0;
            foreach (var item in value.EnumerateArray())
            {
                if (!ValidateValue(itemsSchema, item, $"{path}[{index}]", out error))
                    return false;
                index++;
            }

            return true;
        }

        if (itemsSchema.ValueKind == JsonValueKind.Array)
        {
            var schemas = itemsSchema.EnumerateArray().ToArray();
            var index = 0;
            foreach (var item in value.EnumerateArray())
            {
                if (index >= schemas.Length)
                    return true;

                if (!ValidateValue(schemas[index], item, $"{path}[{index}]", out error))
                    return false;
                index++;
            }
        }

        return true;
    }

    private static bool MatchesDeclaredType(JsonElement schema, JsonElement value)
    {
        if (schema.ValueKind != JsonValueKind.Object
            || !TryGetSchemaProperty(schema, "type", out var type))
        {
            return true;
        }

        return type.ValueKind switch
        {
            JsonValueKind.String => MatchesTypeName(type.GetString(), value),
            JsonValueKind.Array => type
                .EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .Any(typeName => MatchesTypeName(typeName, value)),
            _ => true
        };
    }

    private static bool MatchesTypeName(string? typeName, JsonElement value)
    {
        return typeName?.Trim().ToLowerInvariant() switch
        {
            null or "" => true,
            "object" => value.ValueKind == JsonValueKind.Object,
            "array" => value.ValueKind == JsonValueKind.Array,
            "string" => value.ValueKind == JsonValueKind.String,
            "number" => value.ValueKind == JsonValueKind.Number,
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "null" => value.ValueKind == JsonValueKind.Null,
            _ => true
        };
    }

    private static HashSet<string> ReadRequiredProperties(JsonElement schema)
    {
        if (!schema.TryGetProperty("required", out var required)
            || required.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return required
            .EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static bool ReadAdditionalPropertiesAllowed(JsonElement schema, out JsonElement? additionalPropertiesSchema)
    {
        if (!TryGetSchemaProperty(schema, "additionalProperties", "additional_properties", out var additionalProperties))
        {
            additionalPropertiesSchema = null;
            return true;
        }

        if (additionalProperties.ValueKind == JsonValueKind.Object)
        {
            additionalPropertiesSchema = additionalProperties;
            return true;
        }

        additionalPropertiesSchema = null;
        return additionalProperties.ValueKind != JsonValueKind.False;
    }

    private static Dictionary<string, JsonElement> ReadPropertySchemas(JsonElement schema)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (!schema.TryGetProperty("properties", out var properties)
            || properties.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (var property in properties.EnumerateObject())
            result[property.Name] = property.Value;

        return result;
    }

    private static string DescribeType(JsonElement schema)
    {
        if (!TryGetSchemaProperty(schema, "type", out var type))
            return "any";

        return type.ValueKind switch
        {
            JsonValueKind.String => type.GetString() ?? "any",
            JsonValueKind.Array => string.Join(
                "|",
                type.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString())),
            _ => "any"
        };
    }

    private static bool TryGetSchemaProperty(
        JsonElement schema,
        string primaryName,
        out JsonElement value)
        => TryGetSchemaProperty(schema, primaryName, primaryName, out value);

    private static bool TryGetSchemaProperty(
        JsonElement schema,
        string primaryName,
        string secondaryName,
        out JsonElement value)
    {
        if (schema.TryGetProperty(primaryName, out value))
            return true;

        if (!string.Equals(primaryName, secondaryName, StringComparison.Ordinal)
            && schema.TryGetProperty(secondaryName, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryGetIntConstraint(JsonElement schema, string name, out int value)
    {
        value = 0;
        if (!TryGetSchemaProperty(schema, name, out var element))
            return false;

        return element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value);
    }

    private static bool TryGetDecimalConstraint(JsonElement schema, string name, out decimal value)
    {
        value = 0m;
        if (!TryGetSchemaProperty(schema, name, out var element))
            return false;

        return element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out value);
    }

    private static bool TryGetExclusiveMinimum(JsonElement schema, out decimal value)
    {
        value = 0m;
        if (!TryGetSchemaProperty(schema, "exclusiveMinimum", out var exclusiveMinimum))
            return false;

        if (exclusiveMinimum.ValueKind == JsonValueKind.Number && exclusiveMinimum.TryGetDecimal(out value))
            return true;

        if (exclusiveMinimum.ValueKind == JsonValueKind.True
            && TryGetDecimalConstraint(schema, "minimum", out value))
        {
            return true;
        }

        return false;
    }

    private static bool TryGetExclusiveMaximum(JsonElement schema, out decimal value)
    {
        value = 0m;
        if (!TryGetSchemaProperty(schema, "exclusiveMaximum", out var exclusiveMaximum))
            return false;

        if (exclusiveMaximum.ValueKind == JsonValueKind.Number && exclusiveMaximum.TryGetDecimal(out value))
            return true;

        if (exclusiveMaximum.ValueKind == JsonValueKind.True
            && TryGetDecimalConstraint(schema, "maximum", out value))
        {
            return true;
        }

        return false;
    }

    private static bool MatchesStringFormat(string text, string format)
    {
        return format switch
        {
            "email" => IsValidEmail(text),
            "uri" => Uri.TryCreate(text, UriKind.Absolute, out _),
            "date-time" => DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out _),
            _ => true
        };
    }

    private static bool IsValidEmail(string text)
    {
        try
        {
            _ = new MailAddress(text);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
