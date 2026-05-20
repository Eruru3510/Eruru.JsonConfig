using System.Text.Json;
using System.Text.Json.Serialization;
using Eruru.JsonConfig;

namespace Eruru.JsonConfigSample {

	internal sealed class Program {

		static async Task Main () {
			// 可自定义 JsonSerializerOptions
			// Customizable JsonSerializerOptions
			var jsonContext = new JsonContext (JsonConfig.JsonConfig.JsonSerializerOptions);
			var path = "Config.json";
			using var jsonConfigFileSource = new JsonConfigFileSource (path);
			using var jsonConfig = new JsonConfig<Config, Context> ();
			var context = new Context (jsonContext);
			// 配置来源
			// Configuration source
			await jsonConfig.ConfigurationSource (
				jsonConfigFileSource
				// 配置文件被外部变更后自动重新加载到内存
				// Automatically reload configuration into memory when the file changes externally
				, isAutoReloadWhenSourceChanged: true
				// 自动重新加载的防抖时间，避免外部文件依然处于占用状态导致失败
				// Debounce delay for auto reload to avoid failures while the file is still locked externally
				, autoReloadDebouncerTime: TimeSpan.FromMilliseconds (500)
			)
				// 配置值
				// Configuration value
				.ConfigurationValue (
					// 配置文件不存在时需要创建配置类实例来使用
					// Create a default configuration instance when the config file does not exist
					static jsonConfig => new Config () {
						// 可通过上下文获取一些设置
						// Some settings can be obtained through the context
						Id = jsonConfig.Context?.DefaultId ?? 0
					}
					// 通过 System.Text.Json 的 JsonTypeInfo 实现的序列化/反序列化
					// Serialization/deserialization implemented through System.Text.Json JsonTypeInfo
					, jsonContext.Config
				)
				// 方便链式调用时在 BuildAsync 之前进行一些配置
				// Allows additional configuration before BuildAsync in fluent calls
				.Configuration (static jsonConfig => {
					// 注册改变事件用于检查新旧数据是否有变化，手动更新其他系统的配置等
					// Register change event to compare old/new values or manually update other systems
					jsonConfig.OnChanged += JsonConfig_OnChanged;
				})
				// 构建，此时才会首次加载，如果配置文件不存在则会自动生成
				// Build the configuration. The first load occurs here, and the config file will be created automatically if it does not exist
				.BuildAsync (context).ConfigureAwait (false);
			var id = 0;
			// 读取内存中的配置数据
			// Read configuration data from memory
			await jsonConfig.TryReadAsync ((jsonConfig, value) => {
				id = value.Id;
				return Task.CompletedTask;
			}).ConfigureAwait (false);
			await Task.Delay (1000).ConfigureAwait (false);
			// 修改内存中的配置数据，完成后会自动保存到文件
			// Modify configuration data in memory and automatically save it to the file afterward
			await jsonConfig.TryWriteAsync ((jsonConfig, value) => {
#pragma warning disable CA5394 // 请勿使用不安全的随机性
				value.Id = Random.Shared.Next ();
#pragma warning restore CA5394 // 请勿使用不安全的随机性
				return Task.CompletedTask;
			}).ConfigureAwait (false);
			await Console.In.ReadLineAsync ().ConfigureAwait (false);
			File.Delete (path);
			jsonConfig.OnChanged -= JsonConfig_OnChanged;
		}

		static void JsonConfig_OnChanged (object? sender, JsonConfigOnChangedEventArgs<Config> e) {
#pragma warning disable IDE0079 // 请删除不必要的忽略
#pragma warning disable CA1303 // 请不要将文本作为本地化参数传递
			Console.WriteLine ($"{DateTime.Now:O} {nameof (JsonConfig_OnChanged)}");
#pragma warning restore CA1303 // 请不要将文本作为本地化参数传递
#pragma warning restore IDE0079 // 请删除不必要的忽略
			if (sender is JsonConfig<Config, Context> jsonConfig && jsonConfig.Context != null) {
				Console.WriteLine ($"{nameof (e.OldValue)}: {JsonSerializer.Serialize (e.OldValue, jsonConfig.Context.JsonContext.Config)}");
				Console.WriteLine ($"{nameof (e.Value)}: {JsonSerializer.Serialize (e.Value, jsonConfig.Context.JsonContext.Config)}");
			}
			if (e.Value.Id != e.OldValue?.Id) {
				Console.WriteLine ($"{nameof (e.Value.Id)} has changed to {e.Value.Id} from {e.OldValue?.Id}");
			}
		}

		sealed class Context (JsonContext jsonContext) {

			public JsonContext JsonContext { get; set; } = jsonContext;
			public int DefaultId { get; set; } = 1;

		}

	}

	[JsonSourceGenerationOptions (UseStringEnumConverter = true)]
	[JsonSerializable (typeof (Config))]
#pragma warning disable CA1515 // 考虑将公共类型设为内部类型
	public partial class JsonContext : JsonSerializerContext {
#pragma warning restore CA1515 // 考虑将公共类型设为内部类型

	}

#pragma warning disable CA1515 // 考虑将公共类型设为内部类型
	public class Config {
#pragma warning restore CA1515 // 考虑将公共类型设为内部类型

		public int Id { get; set; }
		public string Name { get; set; } = "Hello, World! 你好，世界！";
#pragma warning disable CA1819 // 属性不应返回数组
		public float[] Floats { get; set; } = [0.0F, 1.1F, 2.2F];
#pragma warning restore CA1819 // 属性不应返回数组
		public DateTime DateTime { get; set; } = DateTime.Now;

	}

}