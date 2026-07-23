using AiSearchAgent;
using Xunit;

namespace AiSearchAgent.Tests;

/// <summary>
/// Guards the agent instructions against accidental drift from the Foundry
/// AI Search Agent definition (the citation and grounding requirements).
/// </summary>
public class AgentInstructionsTests
{
    [Fact]
    public void Prompt_IsNotEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(AgentInstructions.Prompt));
    }

    [Theory]
    [InlineData("Azure AI Search")]
    [InlineData("page numbers")]
    [InlineData("Pages Referenced")]
    [InlineData("Don't make up sources that are not in the index")]
    public void Prompt_ContainsGroundingAndCitationRequirements(string expected)
    {
        Assert.Contains(expected, AgentInstructions.Prompt);
    }
}
