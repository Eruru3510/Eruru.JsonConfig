namespace Eruru.JsonConfig;

public interface IJsonConfigSource : IDisposable {

	event EventHandler? OnChanged;

	Task<Stream?> OpenInputStreamAsync ();

	Task CloseInputStreamAsync (Stream? stream);

	Task<Stream?> OpenOutputStreamAsync ();

	Task CloseOutputStreamAsync (Stream? stream);

	Task BackupAsync ();

	Task DeleteAsync ();

}