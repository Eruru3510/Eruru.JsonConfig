using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Eruru.JsonConfig {

#pragma warning disable CA1724 // 类型名与命名空间名称整体或部分冲突
	public static class JsonConfig {
#pragma warning restore CA1724 // 类型名与命名空间名称整体或部分冲突

		public static JsonSerializerOptions JsonSerializerOptions { get; }

		static JsonConfig () {
			var jsonSerializerOptions = new JsonSerializerOptions () {
				Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, WriteIndented = true
			};
			jsonSerializerOptions.Converters.Add (new DateTimeJsonConverter ());
			JsonSerializerOptions = jsonSerializerOptions;
		}

	}

#pragma warning disable CA1724 // 类型名与命名空间名称整体或部分冲突
	public class JsonConfig<TConfig, TContext> : IDisposable, IAsyncDisposable where TConfig : class {
#pragma warning restore CA1724 // 类型名与命名空间名称整体或部分冲突

		public event EventHandler<JsonConfigOnChangedEventArgs<TConfig>>? OnChanged;
		public TContext? Context { get; private set; }

		bool IsAutoReloadWhenSourceChanged;
		Func<JsonConfig<TConfig, TContext>, TConfig>? OnCreate;
		JsonTypeInfo<TConfig>? JsonTypeInfo;
		IJsonConfigSource? JsonConfigSource;
		TConfig? Value;
		TimeSpan Timeout = TimeSpan.FromSeconds (60);
		TimeSpan AutoReloadDebouncerTime = TimeSpan.FromMilliseconds (1000);
		readonly SemaphoreSlim SemaphoreSlim = new (1, 1);
		int State;
		int BuildState;
		CancellationTokenSource? AutoReloadDebouncerCancellationTokenSource;

		void InternalDispose () {
			OnChanged = null;
			if (JsonConfigSource != null) {
				JsonConfigSource.OnChanged -= JsonConfigSource_OnChanged;
				JsonConfigSource.Dispose ();
			}
			AutoReloadDebouncerCancellationTokenSource?.Dispose ();
		}

		protected virtual void Dispose (bool disposing) {
			if (Interlocked.Exchange (ref State, 1) != 0 || !disposing) {
				return;
			}
			SemaphoreSlim.Wait ();
			try {
				InternalDispose ();
			} finally {
				SemaphoreSlim.Release ();
			}
			SemaphoreSlim.Dispose ();
		}

		public async ValueTask DisposeAsync () {
			if (Interlocked.Exchange (ref State, 1) != 0) {
				return;
			}
			await SemaphoreSlim.WaitAsync ().ConfigureAwait (false);
			try {
				InternalDispose ();
			} finally {
				SemaphoreSlim.Release ();
			}
			SemaphoreSlim.Dispose ();
			GC.SuppressFinalize (this);
		}

		public void Dispose () {
			Dispose (true);
			GC.SuppressFinalize (this);
		}

		public JsonConfig<TConfig, TContext> ConfigureValue (
			Func<JsonConfig<TConfig, TContext>, TConfig> onCreate, JsonTypeInfo<TConfig> jsonTypeInfo
		) {
			OnCreate = onCreate;
			JsonTypeInfo = jsonTypeInfo;
			return this;
		}

		public JsonConfig<TConfig, TContext> ConfigureSource (
			IJsonConfigSource jsonConfigSource, bool isAutoReloadWhenSourceChanged = true, TimeSpan? autoReloadDebouncerTime = null
		) {
			JsonConfigSource = jsonConfigSource;
			IsAutoReloadWhenSourceChanged = isAutoReloadWhenSourceChanged;
			AutoReloadDebouncerTime = autoReloadDebouncerTime.GetValueOrDefault (AutoReloadDebouncerTime);
			return this;
		}

		public JsonConfig<TConfig, TContext> ConfigureContext (TContext context) {
			Context = context;
			return this;
		}

		public JsonConfig<TConfig, TContext> Configure<TState> (
			Action<JsonConfig<TConfig, TContext>, TState> action, TState state, TimeSpan? timeout = null
		) {
#if NET
			ArgumentNullException.ThrowIfNull (action, nameof (action));
#else
			if (action == null) {
				throw new ArgumentNullException (nameof (action));
			}
#endif
			Timeout = timeout.GetValueOrDefault (Timeout);
			action (this, state);
			return this;
		}
		public JsonConfig<TConfig, TContext> Configure (
			Action<JsonConfig<TConfig, TContext>> action, TimeSpan? operationTimeout = null
		) {
			return Configure (static (jsonConfig, state) => state (jsonConfig), action, operationTimeout);
		}

		public async Task<JsonConfig<TConfig, TContext>> BuildAsync (CancellationToken? cancellationToken = null) {
			if (OnCreate == null || JsonTypeInfo == null) {
				throw new ArgumentException ($"Need to {nameof (ConfigureValue)} first");
			}
			if (JsonConfigSource == null) {
				throw new ArgumentException ($"Need to {nameof (ConfigureSource)} first");
			}
			if (Interlocked.Exchange (ref BuildState, 1) != 0) {
				return this;
			}
			CancellationTokenSource? cancellationTokenSource = null;
			try {
				if (cancellationToken == null) {
					cancellationTokenSource = new (Timeout);
					cancellationToken = cancellationTokenSource.Token;
				}
				if (!await TryLoadAsync (cancellationToken).ConfigureAwait (false)) {
					throw new FileLoadException ("Load json file failed");
				}
				await TrySaveAsync (true, cancellationToken).ConfigureAwait (false);
				JsonConfigSource.OnChanged += JsonConfigSource_OnChanged;
				return this;
			} finally {
				cancellationTokenSource?.Dispose ();
			}
		}

		public async Task<bool> TryLoadAsync (CancellationToken? cancellationToken = null) {
			CheckDisposed ();
			CheckBuild ();
			CancellationTokenSource? cancellationTokenSource = null;
			try {
				CancellationToken token;
				if (cancellationToken == null) {
					cancellationTokenSource = new (Timeout);
					token = cancellationTokenSource.Token;
				} else {
					token = cancellationToken.Value;
				}
				JsonConfigOnChangedEventArgs<TConfig>? jsonConfigOnChangedEventArgs = null;
				await SemaphoreSlim.WaitAsync (token).ConfigureAwait (false);
				try {
					CheckDisposed ();
					var inputStream = JsonConfigSource == null ? null
						: await JsonConfigSource.OpenInputStreamAsync ().ConfigureAwait (false);
					try {
						TConfig? value;
						if (inputStream == null) {
							value = Value;
							if (value != null) {
								return true;
							}
							value = OnCreate?.Invoke (this);
							if (value == null) {
								return false;
							}
							jsonConfigOnChangedEventArgs = new (value, Interlocked.Exchange (ref Value, value));
							return true;
						}
						value = await JsonSerializer.DeserializeAsync (inputStream, JsonTypeInfo!).ConfigureAwait (false);
						value ??= OnCreate?.Invoke (this);
						if (value == null) {
							return false;
						}
						jsonConfigOnChangedEventArgs = new (value, Interlocked.Exchange (ref Value, value));
						return true;
					} finally {
						if (JsonConfigSource != null) {
							await JsonConfigSource.CloseInputStreamAsync (inputStream).ConfigureAwait (false);
						}
					}
				} finally {
					SemaphoreSlim.Release ();
					if (jsonConfigOnChangedEventArgs != null) {
						OnChanged?.Invoke (this, jsonConfigOnChangedEventArgs);
					}
				}
			} finally {
				cancellationTokenSource?.Dispose ();
			}
		}

		public async Task<bool> TrySaveAsync (bool cancelAutoReload = true, CancellationToken? cancellationToken = null) {
			CheckDisposed ();
			CheckBuild ();
			CancellationTokenSource? cancellationTokenSource = null;
			try {
				CancellationToken token;
				if (cancellationToken == null) {
					cancellationTokenSource = new (Timeout);
					token = cancellationTokenSource.Token;
				} else {
					token = cancellationToken.Value;
				}
				await SemaphoreSlim.WaitAsync (token).ConfigureAwait (false);
				try {
					CheckDisposed ();
					return await TrySaveAsync (Value, cancelAutoReload).ConfigureAwait (false);
				} finally {
					SemaphoreSlim.Release ();
				}
			} finally {
				cancellationTokenSource?.Dispose ();
			}
		}
		async Task<bool> TrySaveAsync (TConfig? value, bool cancelAutoReload) {
			var isSuccess = false;
			var outputStream = JsonConfigSource == null ? null
				: await JsonConfigSource.OpenOutputStreamAsync ().ConfigureAwait (false);
			try {
				if (outputStream == null || value == null) {
					return false;
				}
				await JsonSerializer.SerializeAsync (outputStream, value, JsonTypeInfo!).ConfigureAwait (false);
				isSuccess = true;
				return true;
			} finally {
				if (JsonConfigSource != null) {
					await JsonConfigSource.CloseOutputStreamAsync (outputStream).ConfigureAwait (false);
					if (isSuccess) {
						await JsonConfigSource.BackupAsync ().ConfigureAwait (false);
					}
				}
				if (cancelAutoReload && IsAutoReloadWhenSourceChanged && outputStream != null) {
					_ = CancelAutoReloadDebouncerAsync ().ContinueWith (static _ => {
						// TODO: handle exception
					}, CancellationToken.None, TaskContinuationOptions.RunContinuationsAsynchronously, TaskScheduler.Default);
				}
			}
		}

		public async Task<bool> TryReadAsync<TState> (
			Func<JsonConfig<TConfig, TContext>, TConfig, TState, Task> func, TState state
		) {
#if NET
			ArgumentNullException.ThrowIfNull (func, nameof (func));
#else
			if (func == null) {
				throw new ArgumentNullException (nameof (func));
			}
#endif
			CheckDisposed ();
			CheckBuild ();
			var value = Volatile.Read (ref Value);
			if (value == null) {
				return false;
			}
			await func (this, value, state).ConfigureAwait (false);
			return true;
		}
		public Task<bool> TryReadAsync<TState> (
			Action<JsonConfig<TConfig, TContext>, TConfig, TState> func, TState state
		) {
			return TryReadAsync<(TState, Action<JsonConfig<TConfig, TContext>, TConfig, TState>)> (
				static (jsonConfig, value, state) => {
					state.Item2 (jsonConfig, value, state.Item1);
				}, (state, func)
			);
		}
		public Task<bool> TryReadAsync (Func<JsonConfig<TConfig, TContext>, TConfig, Task> func) {
			return TryReadAsync (static (jsonConfig, value, state) => state (jsonConfig, value), func);
		}
		public Task<bool> TryReadAsync (Action<JsonConfig<TConfig, TContext>, TConfig> func) {
			return TryReadAsync (static (jsonConfig, value, state) => {
				state (jsonConfig, value);
				return Task.CompletedTask;
			}, func);
		}

		public async Task<bool> TryWriteAsync<TState> (
			Func<JsonConfig<TConfig, TContext>, TConfig, TState, Task> func, TState state, bool cancelAutoReload = true
			, CancellationToken? cancellationToken = null
		) {
#if NET
			ArgumentNullException.ThrowIfNull (func, nameof (func));
#else
			if (func == null) {
				throw new ArgumentNullException (nameof (func));
			}
#endif
			CheckDisposed ();
			CheckBuild ();
			CancellationTokenSource? cancellationTokenSource = null;
			try {
				CancellationToken token;
				if (cancellationToken == null) {
					cancellationTokenSource = new (Timeout);
					token = cancellationTokenSource.Token;
				} else {
					token = cancellationToken.Value;
				}
				JsonConfigOnChangedEventArgs<TConfig>? jsonConfigOnChangedEventArgs = null;
				await SemaphoreSlim.WaitAsync (token).ConfigureAwait (false);
				try {
					CheckDisposed ();
					var value = Value;
					using var jsonDocument = JsonSerializer.SerializeToDocument (value, JsonTypeInfo!);
					value = jsonDocument.Deserialize (JsonTypeInfo!);
					if (value == null) {
						return false;
					}
					await func (this, value, state).ConfigureAwait (false);
					jsonConfigOnChangedEventArgs = new (value, Interlocked.Exchange (ref Value, value));
					return await TrySaveAsync (value, cancelAutoReload).ConfigureAwait (false);
				} finally {
					SemaphoreSlim.Release ();
					if (jsonConfigOnChangedEventArgs != null) {
						OnChanged?.Invoke (this, jsonConfigOnChangedEventArgs);
					}
				}
			} finally {
				cancellationTokenSource?.Dispose ();
			}
		}
		public Task<bool> TryWriteAsync<TState> (
			Action<JsonConfig<TConfig, TContext>, TConfig, TState> func, TState state, bool cancelAutoReload = true
			, CancellationToken? cancellationToken = null
		) {
			return TryWriteAsync<(TState, Action<JsonConfig<TConfig, TContext>, TConfig, TState>)> (
				static (jsonConfig, value, state) => {
					state.Item2 (jsonConfig, value, state.Item1);
					return Task.CompletedTask;
				}, (state, func), cancelAutoReload, cancellationToken
			);
		}
		public Task<bool> TryWriteAsync (
			Func<JsonConfig<TConfig, TContext>, TConfig, Task> func, bool cancelAutoReload = true,
			CancellationToken? cancellationToken = null
		) {
			return TryWriteAsync (
				static (jsonConfig, value, state) => state (jsonConfig, value), func, cancelAutoReload, cancellationToken
			);
		}
		public Task<bool> TryWriteAsync (
			Action<JsonConfig<TConfig, TContext>, TConfig> func, bool cancelAutoReload = true,
			CancellationToken? cancellationToken = null
		) {
			return TryWriteAsync (static (jsonConfig, value, state) => {
				state (jsonConfig, value);
				return Task.CompletedTask;
			}, func, cancelAutoReload, cancellationToken);
		}

		void CheckDisposed () {
			if (Volatile.Read (ref State) == 0) {
				return;
			}
			throw new ObjectDisposedException (nameof (JsonConfig<,>));
		}

		void CheckBuild () {
			if (Volatile.Read (ref BuildState) != 0) {
				return;
			}
			throw new ArgumentException ($"Need to {nameof (BuildAsync)}");
		}

		void JsonConfigSource_OnChanged (object? sender, EventArgs e) {
			if (Volatile.Read (ref BuildState) == 0 || !IsAutoReloadWhenSourceChanged) {
				return;
			}
			_ = AutoReloadDebouncerAsync ().ContinueWith (static _ => {
				// TODO: handle exception
			}, CancellationToken.None, TaskContinuationOptions.RunContinuationsAsynchronously, TaskScheduler.Default);
		}

		async Task CancelAutoReloadDebouncerAsync () {
			await Task.Delay (TimeSpan.FromTicks (AutoReloadDebouncerTime.Ticks / 2)).ConfigureAwait (false);
			var oldCancellationTokenSource = Interlocked.CompareExchange (
				ref AutoReloadDebouncerCancellationTokenSource, null, null
			);
			if (oldCancellationTokenSource == null) {
				return;
			}
			await CancelCancellationTokenSourceAsync (oldCancellationTokenSource).ConfigureAwait (false);
		}

		async Task AutoReloadDebouncerAsync () {
			var cancellationTokenSource = new CancellationTokenSource ();
			var oldCancellationTokenSource = Interlocked.Exchange (
				ref AutoReloadDebouncerCancellationTokenSource, cancellationTokenSource
			);
			if (oldCancellationTokenSource != null) {
				await CancelCancellationTokenSourceAsync (oldCancellationTokenSource).ConfigureAwait (false);
			}
			try {
				await Task.Delay (AutoReloadDebouncerTime, cancellationTokenSource.Token).ConfigureAwait (false);
			} catch (ObjectDisposedException) {
				return;
			} catch (OperationCanceledException) {
				return;
			}
			await TryLoadAsync ().ConfigureAwait (false);
		}

		static async Task CancelCancellationTokenSourceAsync (CancellationTokenSource cancellationTokenSource) {
#if NET
			await cancellationTokenSource.CancelAsync ().ConfigureAwait (false);
#else
			cancellationTokenSource.Cancel ();
#endif
			cancellationTokenSource.Dispose ();
		}

	}

}