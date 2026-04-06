#pragma warning disable OPENAI001

using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects.Agents;
using OpenAI.Responses;
using System.ClientModel;
using System.ClientModel.Primitives;

namespace HERMMapperApp.Services;

public sealed class AzureAiFoundryAgentClient : IAiFoundryAgentClient
{
    public async Task<AiFoundryAgentResponse> GetResponseAsync(
        string projectEndpoint,
        string agentName,
        string? agentVersion,
        string apiKey,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectEndpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        var agentReference = string.IsNullOrWhiteSpace(agentVersion)
            ? new AgentReference(name: agentName)
            : new AgentReference(name: agentName, version: agentVersion);

        var authenticationPolicy = ApiKeyAuthenticationPolicy.CreateBearerAuthorizationPolicy(
            new ApiKeyCredential(apiKey));
        var openAiClient = new ProjectOpenAIClient(
            authenticationPolicy,
            new ProjectOpenAIClientOptions
            {
                Endpoint = BuildProjectOpenAiEndpoint(projectEndpoint)
            });

        var responsesClient = openAiClient.GetProjectResponsesClientForAgent(agentReference);
        var response = await Task
            .Run(() => responsesClient.CreateResponse(prompt), CancellationToken.None)
            .WaitAsync(cancellationToken);
        var output = response.Value.GetOutputText();
        if (string.IsNullOrWhiteSpace(output))
        {
            throw new InvalidOperationException("The Azure AI Foundry agent returned no text output.");
        }

        return new AiFoundryAgentResponse(output);
    }

    private static Uri BuildProjectOpenAiEndpoint(string projectEndpoint) =>
        new($"{projectEndpoint.TrimEnd('/')}/openai/v1");
}
