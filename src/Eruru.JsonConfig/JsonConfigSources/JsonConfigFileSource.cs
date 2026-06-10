namespace Eruru.JsonConfig;

public class JsonConfigFileSource : IJsonConfigSource {

	public event EventHandler? OnChanged;

	readonly string Path;
	readonly string BackupPath;
	readonly FileSystemWatcher? FileSystemWatcher;
	int State;

	public JsonConfigFileSource (string path, bool isEnableFileSystemWatcher = true) {
		var fileInfo = new FileInfo (path);
		if (fileInfo.DirectoryName == null) {
			throw new DirectoryNotFoundException ();
		}
		Path = fileInfo.FullName;
		BackupPath = $"{Path}.bak";
		if (!isEnableFileSystemWatcher) {
			return;
		}
		FileSystemWatcher = new (fileInfo.DirectoryName, $"*{fileInfo.Extension}") {
			NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
		};
		FileSystemWatcher.Changed += FileSystemWatcher_Changed;
		FileSystemWatcher.EnableRaisingEvents = true;
	}

	protected virtual void Dispose (bool disposing) {
		if (Interlocked.Exchange (ref State, 1) != 0 || !disposing) {
			return;
		}
		OnChanged = null;
		if (FileSystemWatcher == null) {
			return;
		}
		FileSystemWatcher.Changed -= FileSystemWatcher_Changed;
		FileSystemWatcher.Dispose ();
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
		if (fileInfo.Length == 0) {
			var backupFileInfo = new FileInfo (BackupPath);
			if (backupFileInfo.Exists) {
				backupFileInfo.CopyTo (fileInfo.FullName, true);
			}
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

	public Task BackupAsync () {
		File.Copy (Path, BackupPath, true);
		return Task.CompletedTask;
	}

	public Task DeleteAsync () {
		File.Delete (Path);
		File.Delete (BackupPath);
		return Task.CompletedTask;
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