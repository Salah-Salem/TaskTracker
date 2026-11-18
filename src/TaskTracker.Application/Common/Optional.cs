using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskTracker.Application.Common;

/// <summary>
/// A PATCH field: distinguishes "not sent" (<see cref="IsSet"/> = false) from "sent" — including
/// sent as <c>null</c>, which is how a client clears a value (e.g. <c>"assigneeId": null</c> unassigns).
/// </summary>
[JsonConverter(typeof(OptionalJsonConverterFactory))]
public readonly struct Optional<T>
{
    public Optional(T value) { Value = value; IsSet = true; }

    public bool IsSet { get; }
    public T Value { get; }

    public static implicit operator Optional<T>(T value) => new(value);
}

public sealed class OptionalJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type t) => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Optional<>);

    public override JsonConverter CreateConverter(Type t, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(OptionalJsonConverter<>).MakeGenericType(t.GetGenericArguments()[0]))!;

    private sealed class OptionalJsonConverter<T> : JsonConverter<Optional<T>>
    {
        // Called only when the property is present in the JSON, so reaching Read means "IsSet".
        public override bool HandleNull => true;

        public override Optional<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            new(JsonSerializer.Deserialize<T>(ref reader, options)!);

        public override void Write(Utf8JsonWriter writer, Optional<T> value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value.Value, options);
    }
}
