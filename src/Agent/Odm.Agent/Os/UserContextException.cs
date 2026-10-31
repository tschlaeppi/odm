namespace Odm.Agent.Os;

/// <summary>The command could not be started in the context of the signed-in user (nobody signed in, no permission, ...).</summary>
public sealed class UserContextException(string message) : Exception(message);
