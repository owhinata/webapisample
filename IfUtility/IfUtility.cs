namespace IfUtilityLib;

public class IfUtility
{
    // Virtual methods allow test subclasses to intercept calls without interfaces
    public virtual void HandleStart(string json)
    {
        // Empty implementation - functionality moved to MyAppMain
    }

    public virtual void HandleEnd(string json)
    {
        // Empty implementation - functionality moved to MyAppMain
    }
}

