#pragma warning disable IDE0130 // 命名空间与文件夹结构不匹配
namespace Eruru.JsonConfig {
#pragma warning restore IDE0130 // 命名空间与文件夹结构不匹配

	public class JsonConfigOnChangedEventArgs<TConfig> (TConfig value, TConfig? oldValue, bool isAutoReload) : EventArgs {

		public TConfig Value { get; } = value;
		public TConfig? OldValue { get; } = oldValue;
		public bool IsAutoReload { get; } = isAutoReload;

	}

}