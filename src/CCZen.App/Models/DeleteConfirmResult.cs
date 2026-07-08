namespace CCZen.App.Models;

/// <summary>Result of the delete confirmation dialog.</summary>
public sealed record DeleteConfirmResult(bool Confirmed, bool UseQuarantine);
