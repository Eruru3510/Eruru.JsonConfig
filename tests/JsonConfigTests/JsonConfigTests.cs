using System.Text.Json;
using System.Text.Json.Serialization;
using Eruru.JsonConfig;

namespace JsonConfigTests {

#pragma warning disable CA1724 // 类型名与命名空间名称整体或部分冲突
	public class JsonConfigTests (ITestOutputHelper testOutputHelper) {
#pragma warning restore CA1724 // 类型名与命名空间名称整体或部分冲突

		readonly ITestOutputHelper TestOutputHelper = testOutputHelper;

		[Fact]
		public async Task FirstStart () {
			var path = Path.GetRandomFileName ();
			var context = new Context ();
			var jsonContext = new JsonContext (new (JsonConfig.JsonSerializerOptions));
			using var jsonConfigFileSource = new JsonConfigFileSource (path);
			using var jsonConfig = new JsonConfig<Config, Context> ();
			await jsonConfig.ConfigureValue (static jsonConfig => new Config (), jsonContext.Config)
				.ConfigureSource (
					jsonConfigFileSource, TimeSpan.FromMilliseconds (50), JsonConfig_OnSaved,
					true, TimeSpan.FromMilliseconds (50)
				)
				.ConfigureContext (context)
				.Configure (static jsonConfig => {
					jsonConfig.OnChanged += JsonConfig_OnChanged;
				})
				.BuildAsync (TestContext.Current.CancellationToken);
			try {
				await Task.Delay (TimeSpan.FromMilliseconds (200), TestContext.Current.CancellationToken);
				Assert.True (File.Exists (path));
				Assert.Equal (1, context.OnChangedCounter);
				Assert.Equal (1, context.OnSavedCounter);
			} finally {
				await jsonConfigFileSource.DeleteAsync ();
				Assert.False (File.Exists (path));
			}
			jsonConfig.OnChanged -= JsonConfig_OnChanged;
		}

		[Fact]
		public async Task Read () {
			var path = Path.GetRandomFileName ();
			var context = new Context ();
			var jsonContext = new JsonContext (new (JsonConfig.JsonSerializerOptions));
			using var jsonConfigFileSource = new JsonConfigFileSource (path);
			using var jsonConfig = new JsonConfig<Config, Context> ();
			await File.WriteAllTextAsync (path, JsonSerializer.Serialize (new Config () {
				Text = nameof (JsonConfig)
			}, jsonContext.Config), TestContext.Current.CancellationToken);
			try {
				await jsonConfig.ConfigureValue (static jsonConfig => new Config (), jsonContext.Config)
					.ConfigureSource (
						jsonConfigFileSource, TimeSpan.FromMilliseconds (50), JsonConfig_OnSaved,
						true, TimeSpan.FromMilliseconds (50)
					)
					.ConfigureContext (context)
					.Configure (static jsonConfig => {
						jsonConfig.OnChanged += JsonConfig_OnChanged;
					})
					.BuildAsync (TestContext.Current.CancellationToken);
				Assert.True (await jsonConfig.TryReadAsync (static (jsonConfig, value) => {
					Assert.Equal (nameof (JsonConfig), value.Text);
				}));
				await Task.Delay (TimeSpan.FromMilliseconds (200), TestContext.Current.CancellationToken);
				Assert.True (File.Exists (path));
				Assert.Equal (1, context.OnChangedCounter);
				Assert.Equal (0, context.OnSavedCounter);
			} finally {
				await jsonConfigFileSource.DeleteAsync ();
				Assert.False (File.Exists (path));
			}
			jsonConfig.OnChanged -= JsonConfig_OnChanged;
		}

		[Fact]
		public async Task Write () {
			var path = Path.GetRandomFileName ();
			var context = new Context ();
			var jsonContext = new JsonContext (new (JsonConfig.JsonSerializerOptions));
			using var jsonConfigFileSource = new JsonConfigFileSource (path);
			using var jsonConfig = new JsonConfig<Config, Context> ();
			await jsonConfig.ConfigureValue (static jsonConfig => new Config (), jsonContext.Config)
				.ConfigureSource (
					jsonConfigFileSource, TimeSpan.FromMilliseconds (50), JsonConfig_OnSaved,
					true, TimeSpan.FromMilliseconds (50)
				)
				.ConfigureContext (context)
				.Configure (static jsonConfig => {
					jsonConfig.OnChanged += JsonConfig_OnChanged;
				})
				.BuildAsync (TestContext.Current.CancellationToken);
			try {
				await Task.Delay (TimeSpan.FromMilliseconds (200), TestContext.Current.CancellationToken);
				Assert.True (await jsonConfig.TryWriteAsync (static (jsonConfig, value) => {
					value.Text = nameof (JsonConfig);
				}, false, TestContext.Current.CancellationToken));
				Assert.True (await jsonConfig.TryReadAsync (static (jsonConfig, value) => {
					Assert.Equal (nameof (JsonConfig), value.Text);
				}));
				await Task.Delay (TimeSpan.FromMilliseconds (200), TestContext.Current.CancellationToken);
				Assert.True (File.Exists (path));
				Assert.Equal (3, context.OnChangedCounter);
				Assert.Equal (2, context.OnSavedCounter);
			} finally {
				await jsonConfigFileSource.DeleteAsync ();
				Assert.False (File.Exists (path));
			}
			jsonConfig.OnChanged -= JsonConfig_OnChanged;
		}

		[Fact]
		public async Task MemoryStream () {
			var context = new Context ();
			var jsonContext = new JsonContext (new (JsonConfig.JsonSerializerOptions));
			using var jsonConfigMemorySource = new JsonConfigMemorySource ();
			using var jsonConfig = new JsonConfig<Config, Context> ();
			await jsonConfig.ConfigureValue (static jsonConfig => new Config (), jsonContext.Config)
				.ConfigureSource (
					jsonConfigMemorySource, TimeSpan.FromMilliseconds (50), JsonConfig_OnSaved,
					true, TimeSpan.FromMilliseconds (50)
				)
				.ConfigureContext (context)
				.Configure (static jsonConfig => {
					jsonConfig.OnChanged += JsonConfig_OnChanged;
				})
				.BuildAsync (TestContext.Current.CancellationToken);
			await Task.Delay (TimeSpan.FromMilliseconds (200), TestContext.Current.CancellationToken);
			Assert.True (await jsonConfig.TryWriteAsync (static (jsonConfig, value) => {
				value.Text = nameof (JsonConfig);
			}, false, TestContext.Current.CancellationToken));
			Assert.True (await jsonConfig.TryReadAsync (static (jsonConfig, value) => {
				Assert.Equal (nameof (JsonConfig), value.Text);
			}));
			await Task.Delay (TimeSpan.FromMilliseconds (200), TestContext.Current.CancellationToken);
			Assert.Equal (2, context.OnChangedCounter);
			Assert.Equal (2, context.OnSavedCounter);
			jsonConfig.OnChanged -= JsonConfig_OnChanged;
		}

		[Fact]
		public async Task CancelAutoReload () {
			var path = Path.GetRandomFileName ();
			var context = new Context ();
			var jsonContext = new JsonContext (new (JsonConfig.JsonSerializerOptions));
			using var jsonConfigFileSource = new JsonConfigFileSource (path);
			using var jsonConfig = new JsonConfig<Config, Context> ();
			await jsonConfig.ConfigureValue (static jsonConfig => new Config (), jsonContext.Config)
				.ConfigureSource (
					jsonConfigFileSource, TimeSpan.FromMilliseconds (50), JsonConfig_OnSaved,
					true, TimeSpan.FromMilliseconds (50)
				)
				.ConfigureContext (context)
				.Configure (static jsonConfig => {
					jsonConfig.OnChanged += JsonConfig_OnChanged;
				})
				.BuildAsync (TestContext.Current.CancellationToken);
			await Task.Delay (TimeSpan.FromMilliseconds (200), TestContext.Current.CancellationToken);
			Assert.True (await jsonConfig.TryWriteAsync (static (jsonConfig, value) => {
				value.Text = nameof (JsonConfig);
			}, true, TestContext.Current.CancellationToken));
			Assert.True (await jsonConfig.TryReadAsync (static (jsonConfig, value) => {
				Assert.Equal (nameof (JsonConfig), value.Text);
			}));
			await Task.Delay (TimeSpan.FromMilliseconds (200), TestContext.Current.CancellationToken);
			Assert.Equal (2, context.OnChangedCounter);
			Assert.Equal (2, context.OnSavedCounter);
			jsonConfig.OnChanged -= JsonConfig_OnChanged;
		}

		[Fact]
		public async Task ThreadSafe () {
			var context = new Context ();
			var jsonContext = new JsonContext (new (JsonConfig.JsonSerializerOptions));
			using var jsonConfigMemorySource = new JsonConfigMemorySource ();
			using var jsonConfig = new JsonConfig<Config, Context> ();
			await jsonConfig.ConfigureValue (static jsonConfig => new Config (), jsonContext.Config)
				.ConfigureSource (
					jsonConfigMemorySource, TimeSpan.FromMilliseconds (50), JsonConfig_OnSaved,
					true, TimeSpan.FromMilliseconds (50)
				)
				.ConfigureContext (context)
				.Configure (static jsonConfig => {
					jsonConfig.OnChanged += JsonConfig_OnChanged;
				})
				.BuildAsync (TestContext.Current.CancellationToken);
			var count = 100;
			var readCounter = 0;
			var writeCounter = 0;
			var writeCounter1 = 0;
			await Task.WhenAll (
				Task.Run (async () => {
					while (Volatile.Read (ref writeCounter) + Volatile.Read (ref writeCounter1) < count) {
						Interlocked.Increment (ref readCounter);
						Assert.True (await jsonConfig.TryReadAsync (static (jsonConfig, value) => {

						}).ConfigureAwait (false));
					}
				}, TestContext.Current.CancellationToken)
				, Task.Run (async () => {
					for (var i = 0; i < count; i++) {
						Interlocked.Increment (ref writeCounter);
						Assert.True (await jsonConfig.TryWriteAsync (static (jsonConfig, value) => {
							value.Counter++;
						}, true, TestContext.Current.CancellationToken).ConfigureAwait (false));
						await Task.Delay (TimeSpan.FromMilliseconds (1)).ConfigureAwait (false);
					}
				}, TestContext.Current.CancellationToken)
				, Task.Run (async () => {
					for (var i = 0; i < count; i++) {
						Interlocked.Increment (ref writeCounter1);
						Assert.True (await jsonConfig.TryWriteAsync (static (jsonConfig, value) => {
							value.Counter--;
						}, true, TestContext.Current.CancellationToken).ConfigureAwait (false));
						await Task.Delay (TimeSpan.FromMilliseconds (1)).ConfigureAwait (false);
					}
				}, TestContext.Current.CancellationToken)
			);
			TestOutputHelper.WriteLine ($"{nameof (readCounter)}: {readCounter:#,0.##}");
			Assert.True (await jsonConfig.TryReadAsync (static (jsonConfig, value) => {
				Assert.Equal (0, value.Counter);
			}));
			await Task.Delay (TimeSpan.FromMilliseconds (200), TestContext.Current.CancellationToken);
			Assert.Equal ((count * 2) + 1, context.OnChangedCounter);
			Assert.Equal (2, context.OnSavedCounter);
			jsonConfig.OnChanged -= JsonConfig_OnChanged;
		}

		static void JsonConfig_OnChanged (object? sender, JsonConfigOnChangedEventArgs<Config> e) {
			if (sender is not JsonConfig<Config, Context> jsonConfig || jsonConfig.Context == null) {
				return;
			}
			Interlocked.Increment (ref jsonConfig.Context.OnChangedCounter);
		}

		static void JsonConfig_OnSaved (JsonConfig<Config, Context>? jsonConfig) {
			if (jsonConfig?.Context is not Context context) {
				return;
			}
			Interlocked.Increment (ref context.OnSavedCounter);
		}

	}

	internal sealed class Context {

		public int OnChangedCounter;
		public int OnSavedCounter;

	}

	[JsonSerializable (typeof (Config))]
#pragma warning disable CA1515 // 考虑将公共类型设为内部类型
	public partial class JsonContext : JsonSerializerContext {
#pragma warning restore CA1515 // 考虑将公共类型设为内部类型

	}

#pragma warning disable CA1515 // 考虑将公共类型设为内部类型
	public sealed class Config {
#pragma warning restore CA1515 // 考虑将公共类型设为内部类型

		public string Text { get; set; } = string.Empty;
		public int Counter { get; set; }

	}

}