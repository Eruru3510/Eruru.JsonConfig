#pragma warning disable IDE0130 // 命名空间与文件夹结构不匹配
namespace Eruru.JsonConfig {
#pragma warning restore IDE0130 // 命名空间与文件夹结构不匹配

	public class JsonConfigFileSource : IJsonConfigSource {

		public event EventHandler? OnChanged;

		readonly string Path;
		readonly FileSystemWatcher? FileSystemWatcher;
		int State;

		public JsonConfigFileSource (string path) {
			var fileInfo = new FileInfo (path);
			Path = fileInfo.FullName;
			if (fileInfo.DirectoryName == null) {
				return;
			}
			FileSystemWatcher = new (fileInfo.DirectoryName, $"*{fileInfo.Extension}") {
				NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
			};
			FileSystemWatcher.Changed += FileSystemWatcher_Changed;
			FileSystemWatcher.EnableRaisingEvents = true;
		}

		protected virtual void Dispose (bool disposing) {
			if (Interlocked.Exchange (ref State, 1) != 0) {
				return;
			}
			if (disposing) {
				OnChanged = null;
				if (FileSystemWatcher != null) {
					FileSystemWatcher.Changed -= FileSystemWatcher_Changed;
					FileSystemWatcher.Dispose ();
				}
			}
		}

		public void Dispose () {
			Dispose (true);
			GC.SuppressFinalize (this);
		}

		public Task<Stream?> OpenInputStreamAsync () {
			CheckDisposed ();
			var fileInfo = new FileInfo (Path);
			if (!fileInfo.Exists) {
				return Task.FromResult<Stream?> (null);
			}
			return Task.FromResult<Stream?> (fileInfo.Open (FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
		}

		public Task CloseInputStreamAsync (Stream? stream) {
			if (stream == null) {
				return Task.CompletedTask;
			}
#if NET
			return stream.DisposeAsync ().AsTask ();
#else
			stream.Dispose ();
			return Task.CompletedTask;
#endif
		}

		public Task<Stream?> OpenOutputStreamAsync () {
			CheckDisposed ();
			return Task.FromResult<Stream?> (File.Open (Path, FileMode.Create, FileAccess.Write, FileShare.Read));
		}

		public Task CloseOutputStreamAsync (Stream? stream) {
			if (stream == null) {
				return Task.CompletedTask;
			}
#if NET
			return stream.DisposeAsync ().AsTask ();
#else
			stream.Dispose ();
			return Task.CompletedTask;
#endif
		}

		void CheckDisposed () {
			if (Volatile.Read (ref State) == 0) {
				return;
			}
			throw new ObjectDisposedException (nameof (JsonConfigFileSource));
		}

		void FileSystemWatcher_Changed (object sender, FileSystemEventArgs e) {
			if (Volatile.Read (ref State) != 0 || e.FullPath != Path) {
				return;
			}
			OnChanged?.Invoke (this, EventArgs.Empty);
		}

	}

}