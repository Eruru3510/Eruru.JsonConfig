using System.Text.Json;
using System.Text.Json.Serialization;

#pragma warning disable IDE0130 // 命名空间与文件夹结构不匹配
namespace Eruru.JsonConfig {
#pragma warning restore IDE0130 // 命名空间与文件夹结构不匹配

	public class DateTimeJsonConverter : JsonConverter<DateTime> {

		public override DateTime Read (ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
			switch (reader.TokenType) {
				case JsonTokenType.String: {
					if (!reader.TryGetDateTime (out var dateTime)) {
						throw new InvalidCastException ();
					}
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
					throw new InvalidDataException ();
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

}