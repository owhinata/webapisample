namespace MyAppMain;

/// <summary>
/// Allows controllers to access the command pipeline once registered.
/// </summary>
internal interface ICommandPipelineAware
{
    /// <summary>
    /// Provides the pipeline instance to the controller.
    /// </summary>
    /// <param name="pipeline">Pipeline the controller can use to issue commands.</param>
    void AttachPipeline(CommandPipeline pipeline);

    /// <summary>
    /// Removes the pipeline reference when the controller is unregistered.
    /// </summary>
    /// <param name="pipeline">Pipeline that is being detached.</param>
    void DetachPipeline(CommandPipeline pipeline);
}
