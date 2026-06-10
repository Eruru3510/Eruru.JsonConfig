using System.Text.Json;
using System.Text.Json.Serialization;

namespace Eruru.JsonConfig;

public class DateTimeJsonConverter : JsonConverter<DateTime> {

	public override DateTime Read (ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
		switch (reader.TokenType) {
			case JsonTokenType.String: {
				var dateTime = reader.GetDateTime ();
				switch (dateTime.Kind) {
					case DateTimeKind.Unspecified:
						dateTime = new (dateTime.Ticks, DateTimeKind.Local);
						break;
					case DateTimeKind.Utc:
						dateTime = dateTime.ToLocalTime ();
						break;
				}
				return dateTime;
			}
			default:
				throw new InvalidDataException ($"The expected value is a JSON string. \"{DateTime.Now:O}\"");
		}
	}

	public override void Write (Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) {
#if NET
		ArgumentNullException.ThrowIfNull (writer, nameof (writer));
#else
		if (writer == null) {
			throw new ArgumentNullException (nameof (writer));
		}
#endif
		if (value.Kind == DateTimeKind.Unspecified) {
			value = new (value.Ticks, DateTimeKind.Local);
		}
		writer.WriteStringValue (value);
	}

}