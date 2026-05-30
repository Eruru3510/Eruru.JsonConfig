using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Eruru.Debouncer;

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
		public Action<JsonConfig<TConfig, TContext>>? OnSaved { get; private set; }
		public TContext? Context { get; private set; }

		bool IsAutoReloadWhenSourceChanged;
		Func<JsonConfig<TConfig, TContext>, TConfig>? OnCreate;
		JsonTypeInfo<TConfig>? JsonTypeInfo;
		IJsonConfigSource? JsonConfigSource;
		TConfig? Value;
		TimeSpan Timeout = TimeSpan.FromSeconds (60);
		TimeSpan SaveDebouncerTime = TimeSpan.FromMilliseconds (500);
		TimeSpan AutoReloadDebouncerTime = TimeSpan.FromMilliseconds (500);
		readonly SemaphoreSlim SemaphoreSlim = new (1, 1);
		int State;
		int BuildState;
		Debouncer<JsonConfig<TConfig, TContext>, TConfig>? AutoReloadDebouncer;
		Debouncer<JsonConfig<TConfig, TContext>, TConfig>? CancelAutoReloadDebouncer;
		Debouncer<JsonConfig<TConfig, TContext>, (bool, CancellationToken)>? SaveDebouncer;

		void InternalDispose () {
			OnChanged = null;
			if (JsonConfigSource != null) {
				JsonConfigSource.OnChanged -= JsonConfigSource_OnChanged;
				JsonConfigSource.Dispose ();
			}
			AutoReloadDebouncer?.Dispose ();
			CancelAutoReloadDebouncer?.Dispose ();
			SaveDebouncer?.Dispose ();
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
		public void Dispose () {
			Dispose (true);
			GC.SuppressFinalize (this);
		}

		public async ValueTask DisposeAsync () {
			if (Interlocked.Exchange (ref State, 1) != 0) {
				return;
			}
			await SemaphoreSlim.WaitAsync (Timeout).ConfigureAwait (false);
			try {
				InternalDispose ();
			} finally {
				SemaphoreSlim.Release ();
			}
			SemaphoreSlim.Dispose ();
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
			IJsonConfigSource jsonConfigSource,
			TimeSpan? saveDebouncerTime = null, Action<JsonConfig<TConfig, TContext>>? onSaved = null,
			bool isAutoReloadWhenSourceChanged = true, TimeSpan? autoReloadDebouncerTime = null
		) {
			JsonConfigSource = jsonConfigSource;
			SaveDebouncerTime = saveDebouncerTime.GetValueOrDefault (SaveDebouncerTime);
			OnSaved = onSaved;
			IsAutoReloadWhenSourceChanged = isAutoReloadWhenSourceChanged;
			AutoReloadDebouncerTime = autoReloadDebouncerTime.GetValueOrDefault (AutoReloadDebouncerTime);
			AutoReloadDebouncer = new (AutoReloadDebouncerTime, this);
			CancelAutoReloadDebouncer = new (new (AutoReloadDebouncerTime.Ticks / 2), this);
			SaveDebouncer = new (SaveDebouncerTime, this);
			return this;
		}

		public JsonConfig<TConfig, TContext> ConfigureContext (TContext? context) {
			Context = context;
			return this;
		}

		public JsonConfig<TConfig, TContext> Configure<TState> (
			Action<JsonConfig<TConfig, TContext>, TState> callback, TState state, TimeSpan? timeout = null
		) {
#if NET
			ArgumentNullException.ThrowIfNull (callback, nameof (callback));
#else
			if (callback == null) {
				throw new ArgumentNullException (nameof (callback));
			}
#endif
			Timeout = timeout.GetValueOrDefault (Timeout);
			callback (this, state);
			return this;
		}
		public JsonConfig<TConfig, TContext> Configure (
			Action<JsonConfig<TConfig, TContext>> callback, TimeSpan? timeout = null
		) {
			return Configure (static (jsonConfig, state) => state (jsonConfig), callback, timeout);
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
				JsonConfigSource.OnChanged += JsonConfigSource_OnChanged;
				return this;
			} finally {
				cancellationTokenSource?.Dispose ();
			}
		}

		public Task<bool> TryLoadAsync (CancellationToken? cancellationToken = null) {
			return TryLoadAsync (false, cancellationToken);
		}
		async Task<bool> TryLoadAsync (bool isAutoReload, CancellationToken? cancellationToken = null) {
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
				var isSaved = false;
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
								isSaved = await TrySaveAsync (value, true).ConfigureAwait (false);
								return true;
							}
							value = OnCreate?.Invoke (this);
							if (value == null) {
								return false;
							}
							jsonConfigOnChangedEventArgs = new (value, Interlocked.Exchange (ref Value, value), isAutoReload);
							isSaved = await TrySaveAsync (value, true).ConfigureAwait (false);
							return true;
						}
						value = await JsonSerializer.DeserializeAsync (inputStream, JsonTypeInfo!).ConfigureAwait (false);
						value ??= OnCreate?.Invoke (this);
						if (value == null) {
							return false;
						}
						jsonConfigOnChangedEventArgs = new (value, Interlocked.Exchange (ref Value, value), isAutoReload);
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
					if (isSaved) {
						OnSaved?.Invoke (this);
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
				var isSaved = false;
				await SemaphoreSlim.WaitAsync (token).ConfigureAwait (false);
				try {
					CheckDisposed ();
					isSaved = await TrySaveAsync (Value, cancelAutoReload).ConfigureAwait (false);
					return isSaved;
				} finally {
					SemaphoreSlim.Release ();
					if (isSaved) {
						OnSaved?.Invoke (this);
					}
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
					CancelAutoReloadDebouncer?.Post (static (debouncer, state) => {
						debouncer.Context?.AutoReloadDebouncer?.Cancel ();
						return Task.CompletedTask;
					});
				}
			}
		}

		public async Task<bool> TryReadAsync<TState> (
			Func<JsonConfig<TConfig, TContext>, TConfig, TState, Task> callbackAsync, TState state
		) {
#if NET
			ArgumentNullException.ThrowIfNull (callbackAsync, nameof (callbackAsync));
#else
			if (callbackAsync == null) {
				throw new ArgumentNullException (nameof (callbackAsync));
			}
#endif
			CheckDisposed ();
			CheckBuild ();
			var value = Volatile.Read (ref Value);
			if (value == null) {
				return false;
			}
			await callbackAsync (this, value, state).ConfigureAwait (false);
			return true;
		}
		public Task<bool> TryReadAsync<TState> (
			Action<JsonConfig<TConfig, TContext>, TConfig, TState> callback, TState state
		) {
			return TryReadAsync<(TState, Action<JsonConfig<TConfig, TContext>, TConfig, TState>)> (
				static (jsonConfig, value, state) => {
					state.Item2 (jsonConfig, value, state.Item1);
					return Task.CompletedTask;
				}, (state, callback)
			);
		}
		public Task<bool> TryReadAsync (Func<JsonConfig<TConfig, TContext>, TConfig, Task> callbackAsync) {
			return TryReadAsync (static (jsonConfig, value, state) => state (jsonConfig, value), callbackAsync);
		}
		public Task<bool> TryReadAsync (Action<JsonConfig<TConfig, TContext>, TConfig> callback) {
			return TryReadAsync (static (jsonConfig, value, state) => {
				state (jsonConfig, value);
				return Task.CompletedTask;
			}, callback);
		}

		public bool TryRead<TState> (Action<JsonConfig<TConfig, TContext>, TConfig, TState> callback, TState state) {
#if NET
			ArgumentNullException.ThrowIfNull (callback, nameof (callback));
#else
			if (callback == null) {
				throw new ArgumentNullException (nameof (callback));
			}
#endif
			CheckDisposed ();
			CheckBuild ();
			var value = Volatile.Read (ref Value);
			if (value == null) {
				return false;
			}
			callback (this, value, state);
			return true;
		}
		public bool TryRead (Action<JsonConfig<TConfig, TContext>, TConfig> callback) {
			return TryRead (static (jsonConfig, value, state) => state (jsonConfig, value), callback);
		}

		public T Read<T, TState> (Func<JsonConfig<TConfig, TContext>, TConfig?, TState, T> callback, TState state) {
#if NET
			ArgumentNullException.ThrowIfNull (callback, nameof (callback));
#else
			if (callback == null) {
				throw new ArgumentNullException (nameof (callback));
			}
#endif
			CheckDisposed ();
			CheckBuild ();
			return callback (this, Volatile.Read (ref Value), state);
		}
		public T Read<T> (Func<JsonConfig<TConfig, TContext>, TConfig?, T> callback) {
			return Read (static (jsonConfig, value, state) => state (jsonConfig, value), callback);
		}
		public TConfig? Read () {
			CheckDisposed ();
			CheckBuild ();
			return Volatile.Read (ref Value);
		}

		public async Task<bool> TryWriteAsync<TState> (
			Func<JsonConfig<TConfig, TContext>, TConfig, TState, Task> callbackAsync, TState state
			, bool cancelAutoReload = true, CancellationToken? cancellationToken = null
		) {
#if NET
			ArgumentNullException.ThrowIfNull (callbackAsync, nameof (callbackAsync));
#else
			if (callbackAsync == null) {
				throw new ArgumentNullException (nameof (callbackAsync));
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
					await callbackAsync (this, value, state).ConfigureAwait (false);
					jsonConfigOnChangedEventArgs = new (value, Interlocked.Exchange (ref Value, value), false);
					SaveDebouncer?.Post (static async (debouncer, state) => {
						if (debouncer.Context == null) {
							return;
						}
						var isSaved = false;
						await debouncer.Context.SemaphoreSlim.WaitAsync (state.Item2).ConfigureAwait (false);
						try {
							debouncer.Context.CheckDisposed ();
							isSaved = await debouncer.Context.TrySaveAsync (
								debouncer.Context.Value, state.Item1
							).ConfigureAwait (false);
						} finally {
							debouncer.Context.SemaphoreSlim.Release ();
							if (isSaved) {
								debouncer.Context.OnSaved?.Invoke (debouncer.Context);
							}
						}
					}, (cancelAutoReload, token));
					return true;
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
			Action<JsonConfig<TConfig, TContext>, TConfig, TState> callback, TState state, bool cancelAutoReload = true
			, CancellationToken? cancellationToken = null
		) {
			return TryWriteAsync<(TState, Action<JsonConfig<TConfig, TContext>, TConfig, TState>)> (
				static (jsonConfig, value, state) => {
					state.Item2 (jsonConfig, value, state.Item1);
					return Task.CompletedTask;
				}, (state, callback), cancelAutoReload, cancellationToken
			);
		}
		public Task<bool> TryWriteAsync (
			Func<JsonConfig<TConfig, TContext>, TConfig, Task> callbackAsync, bool cancelAutoReload = true,
			CancellationToken? cancellationToken = null
		) {
			return TryWriteAsync (
				static (jsonConfig, value, state) => state (jsonConfig, value), callbackAsync,
				cancelAutoReload, cancellationToken
			);
		}
		public Task<bool> TryWriteAsync (
			Action<JsonConfig<TConfig, TContext>, TConfig> callback, bool cancelAutoReload = true,
			CancellationToken? cancellationToken = null
		) {
			return TryWriteAsync (static (jsonConfig, value, state) => {
				state (jsonConfig, value);
				return Task.CompletedTask;
			}, callback, cancelAutoReload, cancellationToken);
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
			AutoReloadDebouncer?.Post (static async (debouncer, state) => {
				if (debouncer.Context == null) {
					return;
				}
				await debouncer.Context.TryLoadAsync (true).ConfigureAwait (false);
			});
		}

	}

}