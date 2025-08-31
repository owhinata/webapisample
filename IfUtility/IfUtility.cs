namespace IfUtilityLib;

public class IfUtility
{
    // Virtual methods allow test subclasses to intercept calls without interfaces
    public virtual void HandleStart(string json)
    {
        // Implement your external logic call here
    }

    public virtual void HandleEnd(string json)
    {
        // Implement your external logic call here
    }
}

