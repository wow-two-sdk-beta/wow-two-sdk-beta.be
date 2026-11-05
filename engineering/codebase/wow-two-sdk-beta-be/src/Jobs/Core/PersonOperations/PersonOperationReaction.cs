namespace WoW.Two.Sdk.Beta.Be.Jobs.Core.PersonOperations;

/// <summary>Refers to what a person-bound operation does when a signal about that person reaches it.</summary>
public enum PersonOperationReaction
{
    /// <summary>Refers to running on: the work no longer depends on the person being there.</summary>
    Continue,

    /// <summary>Refers to stopping: the lease's token is cancelled.</summary>
    Cancel,
}
