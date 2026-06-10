namespace Eruru.JsonConfig;

public class JsonConfigOnChangedEventArgs<TConfig> (TConfig value, TConfig? oldValue, bool isAutoReload) : EventArgs {

	public TConfig Value { get; } = value;
	public TConfig? OldValue { get; } = oldValue;
	public bool IsAutoReload { get; } = isAutoReload;

}