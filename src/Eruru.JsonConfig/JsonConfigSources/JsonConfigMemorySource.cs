namespace Eruru.JsonConfig;

public class JsonConfigMemorySource : IJsonConfigSource {

	public event EventHandler? OnChanged;

	MemoryStream? MemoryStream;
	int State;

	public JsonConfigMemorySource () {
		OnChanged?.Invoke (this, EventArgs.Empty);
	}

	protected virtual void Dispose (bool disposing) {
		if (Interlocked.Exchange (ref State, 1) != 0 || !disposing) {
			return;
		}
		OnChanged = null;
	}
	public void Dispose () {
		Dispose (true);
		GC.SuppressFinalize (this);
	}

	public Task<Stream?> OpenInputStreamAsync () {
		CheckDisposed ();
		MemoryStream?.Position = 0;
		return Task.FromResult<Stream?> (MemoryStream);
	}

	public Task CloseInputStreamAsync (Stream? stream) {
		return Task.CompletedTask;
	}

	public Task<Stream?> OpenOutputStreamAsync () {
		CheckDisposed ();
		MemoryStream ??= new ();
		MemoryStream.Position = 0;
		MemoryStream.SetLength (0);
		return Task.FromResult<Stream?> (MemoryStream);
	}

	public Task CloseOutputStreamAsync (Stream? stream) {
		return Task.CompletedTask;
	}

	public Task BackupAsync () {
		return Task.CompletedTask;
	}

	public Task DeleteAsync () {
		MemoryStream = null;
		return Task.CompletedTask;
	}

	void CheckDisposed () {
		if (Volatile.Read (ref State) == 0) {
			return;
		}
		throw new ObjectDisposedException (nameof (JsonConfigMemorySource));
	}

}