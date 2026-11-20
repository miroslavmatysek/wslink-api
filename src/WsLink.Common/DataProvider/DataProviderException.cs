namespace WsLink.Common.DataProvider;

/// <summary>
/// Expected failure of a data provider (database unreachable, bad credentials, missing table, timeout).
/// Wraps the driver exception, so callers do not depend on the database driver.
/// </summary>
public class DataProviderException(string message, Exception innerException) : Exception(message, innerException);
