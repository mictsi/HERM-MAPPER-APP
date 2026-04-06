namespace HERMMapperApp.Services;

public interface IAiFoundryAgentClient
{
    Task<AiFoundryAgentResponse> GetResponseAsync(
        string projectEndpoint,
        string agentName,
        string? agentVersion,
        string apiKey,
        string prompt,
        CancellationToken cancellationToken = default);
}

public sealed record AiFoundryAgentResponse(string OutputText);
