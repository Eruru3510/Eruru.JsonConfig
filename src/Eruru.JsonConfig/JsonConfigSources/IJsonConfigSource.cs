#pragma warning disable IDE0130 // 命名空间与文件夹结构不匹配
namespace Eruru.JsonConfig {
#pragma warning restore IDE0130 // 命名空间与文件夹结构不匹配

	public interface IJsonConfigSource : IDisposable {

		event EventHandler? OnChanged;

		Task<Stream?> OpenInputStreamAsync ();

		Task CloseInputStreamAsync (Stream? stream);

		Task<Stream?> OpenOutputStreamAsync ();

		Task CloseOutputStreamAsync (Stream? stream);

	}

}