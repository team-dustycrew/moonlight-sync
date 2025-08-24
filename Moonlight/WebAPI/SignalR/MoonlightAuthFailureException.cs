namespace Moonlight.WebAPI.SignalR;

public class MoonlightAuthFailureException : Exception
{
    public MoonlightAuthFailureException(string reason)
    {
        Reason = reason;
    }

    public string Reason { get; }
}