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
		CancellationTokenSource? AutoReloadDebouncerCancellationTokenSource;
		CancellationTokenSource? CancelAutoReloadDebouncerCancellationTokenSource;
		CancellationTokenSource? SaveDebouncerCancellationTokenSource;

		void InternalDispose () {
			OnChanged = null;
			if (JsonConfigSource != null) {
				JsonConfigSource.OnChanged -= JsonConfigSource_OnChanged;
				JsonConfigSource.Dispose ();
			}
			AutoReloadDebouncerCancellationTokenSource?.Dispose ();
			CancelAutoReloadDebouncerCancellationTokenSource?.Dispose ();
			SaveDebouncerCancellationTokenSource?.Dispose ();
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
			await SemaphoreSlim.WaitAsync (Timeout).ConfigureAwait (false);
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
			IJsonConfigSource jsonConfigSource,
			TimeSpan? saveDebouncerTime = null, Action<JsonConfig<TConfig, TContext>>? onSaved = null,
			bool isAutoReloadWhenSourceChanged = true, TimeSpan? autoReloadDebouncerTime = null
		) {
			JsonConfigSource = jsonConfigSource;
			SaveDebouncerTime = saveDebouncerTime.GetValueOrDefault (SaveDebouncerTime);
			OnSaved = onSaved;
			IsAutoReloadWhenSourceChanged = isAutoReloadWhenSourceChanged;
			AutoReloadDebouncerTime = autoReloadDebouncerTime.GetValueOrDefault (AutoReloadDebouncerTime);
			return this;
		}

		public JsonConfig<TConfig, TContext> ConfigureContext (TContext? context) {
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
			Action<JsonConfig<TConfig, TContext>> action, TimeSpan? timeout = null
		) {
			return Configure (static (jsonConfig, state) => state (jsonConfig), action, timeout);
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
					_ = CancelAutoReloadDebouncerAsync ().ContinueWith (static _ => {
						// TODO: handle exception
					}, CancellationToken.None, TaskContinuationOptions.RunContinuationsAsynchronously, TaskScheduler.Default);
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
		public Task<bool> TryReadAsync<TState> (Action<JsonConfig<TConfig, TContext>, TConfig, TState> func, TState state) {
			return TryReadAsync<(TState, Action<JsonConfig<TConfig, TContext>, TConfig, TState>)> (
				static (jsonConfig, value, state) => {
					state.Item2 (jsonConfig, value, state.Item1);
					return Task.CompletedTask;
				}, (state, func)
			);
		}
		public Task<bool> TryReadAsync (Func<JsonConfig<TConfig, TContext>, TConfig, Task> callbackAsync) {
			return TryReadAsync (static (jsonConfig, value, state) => state (jsonConfig, value), callbackAsync);
		}
		public Task<bool> TryReadAsync (Action<JsonConfig<TConfig, TContext>, TConfig> callbackAsync) {
			return TryReadAsync (static (jsonConfig, value, state) => {
				state (jsonConfig, value);
				return Task.CompletedTask;
			}, callbackAsync);
		}

		public bool TryRead<TState> (Action<JsonConfig<TConfig, TContext>, TConfig, TState> func, TState state) {
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
			func (this, value, state);
			return true;
		}
		public bool TryRead (Action<JsonConfig<TConfig, TContext>, TConfig> func) {
			return TryRead (static (jsonConfig, value, state) => state (jsonConfig, value), func);
		}

		public T Read<T, TState> (Func<JsonConfig<TConfig, TContext>, TConfig?, TState, T> func, TState state) {
#if NET
			ArgumentNullException.ThrowIfNull (func, nameof (func));
#else
			if (func == null) {
				throw new ArgumentNullException (nameof (func));
			}
#endif
			CheckDisposed ();
			CheckBuild ();
			return func (this, Volatile.Read (ref Value), state);
		}
		public T Read<T> (Func<JsonConfig<TConfig, TContext>, TConfig?, T> func) {
			return Read (static (jsonConfig, value, state) => state (jsonConfig, value), func);
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
					_ = SaveDebouncerAsync (
						new (value, Interlocked.Exchange (ref Value, value), false), cancelAutoReload, token
					).ContinueWith (static task => {
						// TODO: handle exception
					}, CancellationToken.None, TaskContinuationOptions.RunContinuationsAsynchronously, TaskScheduler.Default);
					return true;
				} finally {
					SemaphoreSlim.Release ();
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
			Func<JsonConfig<TConfig, TContext>, TConfig, Task> callbackAsync, bool cancelAutoReload = true,
			CancellationToken? cancellationToken = null
		) {
			return TryWriteAsync (
				static (jsonConfig, value, state) => state (jsonConfig, value), callbackAsync,
				cancelAutoReload, cancellationToken
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

		async Task SaveDebouncerAsync (
			JsonConfigOnChangedEventArgs<TConfig> jsonConfigOnChangedEventArgs, bool cancelAutoReload,
			CancellationToken cancellationToken
		) {
			if (SaveDebouncerTime > TimeSpan.Zero) {
				var cancellationTokenSource = new CancellationTokenSource ();
				var oldCancellationTokenSource = Interlocked.Exchange (
					ref SaveDebouncerCancellationTokenSource, cancellationTokenSource
				);
				if (oldCancellationTokenSource != null) {
					await CancelCancellationTokenSourceAsync (oldCancellationTokenSource).ConfigureAwait (false);
				}
				try {
					await Task.Delay (SaveDebouncerTime, cancellationTokenSource.Token).ConfigureAwait (false);
				} catch (ObjectDisposedException) {
					return;
				} catch (OperationCanceledException) {
					return;
				}
			}
			var isSaved = false;
			await SemaphoreSlim.WaitAsync (cancellationToken).ConfigureAwait (false);
			try {
				CheckDisposed ();
				isSaved = await TrySaveAsync (jsonConfigOnChangedEventArgs.Value, cancelAutoReload).ConfigureAwait (false);
			} finally {
				SemaphoreSlim.Release ();
				if (isSaved) {
					OnSaved?.Invoke (this);
				}
			}
			OnChanged?.Invoke (this, jsonConfigOnChangedEventArgs);
		}

		async Task CancelAutoReloadDebouncerAsync () {
			if (AutoReloadDebouncerTime <= TimeSpan.Zero) {
				return;
			}
			var cancellationTokenSource = new CancellationTokenSource ();
			var oldCancellationTokenSource = Interlocked.Exchange (
				ref CancelAutoReloadDebouncerCancellationTokenSource, cancellationTokenSource
			);
			if (oldCancellationTokenSource != null) {
				await CancelCancellationTokenSourceAsync (oldCancellationTokenSource).ConfigureAwait (false);
			}
			try {
				await Task.Delay (
					TimeSpan.FromTicks (AutoReloadDebouncerTime.Ticks / 2), cancellationTokenSource.Token
				).ConfigureAwait (false);
			} catch (ObjectDisposedException) {
				return;
			} catch (OperationCanceledException) {
				return;
			}
			oldCancellationTokenSource = Volatile.Read (ref AutoReloadDebouncerCancellationTokenSource);
			if (oldCancellationTokenSource == null) {
				return;
			}
			await CancelCancellationTokenSourceAsync (oldCancellationTokenSource).ConfigureAwait (false);
		}

		async Task AutoReloadDebouncerAsync () {
			if (AutoReloadDebouncerTime > TimeSpan.Zero) {
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
			}
			await TryLoadAsync (true).ConfigureAwait (false);
		}

		static async Task CancelCancellationTokenSourceAsync (CancellationTokenSource cancellationTokenSource) {
			if (cancellationTokenSource.IsCancellationRequested) {
				return;
			}
#if NET
			await cancellationTokenSource.CancelAsync ().ConfigureAwait (false);
#else
			cancellationTokenSource.Cancel ();
#endif
			cancellationTokenSource.Dispose ();
		}

	}

}